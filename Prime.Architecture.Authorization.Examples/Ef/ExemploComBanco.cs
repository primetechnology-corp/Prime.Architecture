using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Prime.Architecture.Authorization.Auditing;
using Prime.Architecture.Authorization.Discovery;
using Prime.Architecture.Authorization.EntityFramework;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using StackExchange.Redis;

namespace Prime.Architecture.Authorization.Examples.Ef;

// ---- Suas entidades (o pacote só conhece ids de usuário e de perfil) ----
public class Usuario
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public int PerfilId { get; set; }
    public bool Ativo { get; set; } = true;
}

public class AuditoriaPermissao
{
    public int Id { get; set; }
    public DateTime Quando { get; set; } = DateTime.UtcNow;
    public string Tipo { get; set; } = "";
    public string? Ator { get; set; }
    public string? Alvo { get; set; }
    public string? Permissao { get; set; }
    public string? Detalhe { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<AuditoriaPermissao> Auditoria => Set<AuditoriaPermissao>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AplicarResourceAuthorization();   // Permissoes, PerfisPermissoes, UsuariosPermissoes
    }
}

// ---- Necessário para os comandos "dotnet ef" (o app pode não registrar o DbContext no host) ----
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=app.db").Options);
}

// ---- Perfil VIGENTE do usuário, lido do banco (não do token) ----
public class UsuarioPerfilDoBanco(AppDbContext db) : IUsuarioPerfilProvider
{
    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return null;

        // null (inexistente, inativo ou bloqueado) => o usuário fica SEM nenhuma permissão
        return await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == id && u.Ativo)
            .Select(u => new UsuarioPerfilInfo(u.Id, u.PerfilId))
            .FirstOrDefaultAsync(ct);
    }
}

// ---- Auditoria das alterações administrativas gravada em tabela ----
public class AuditoriaEmTabela(AppDbContext db) : IPermissaoAuditor
{
    public async Task RegistrarAsync(PermissaoAuditoriaEvento evento, CancellationToken ct = default)
    {
        db.Auditoria.Add(new AuditoriaPermissao
        {
            Tipo = evento.Tipo.ToString(), Ator = evento.Ator, Alvo = evento.Alvo,
            Permissao = evento.Permissao, Detalhe = evento.Detalhe
        });
        await db.SaveChangesAsync(ct);
    }
}

public static class ExemploComBanco
{
    /// <summary>Registro completo no modo com banco.</summary>
    public static void Registrar(WebApplicationBuilder builder, bool variasInstancias)
    {
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite("Data Source=app.db"));

        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization();
        builder.Services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuarioPerfilDoBanco>(o =>
        {
            o.DuracaoCache = TimeSpan.FromMinutes(5);               // permissões do perfil/usuário
            o.DuracaoCachePerfilUsuario = TimeSpan.FromMinutes(1);  // consulta de usuário/perfil
        });

        builder.Services.AddScoped<IPermissaoAuditor, AuditoriaEmTabela>();   // substitui o auditor de log

        if (variasInstancias)
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(
                _ => ConnectionMultiplexer.Connect(builder.Configuration["Redis:Connection"]!));
            builder.Services.AddPermissaoInvalidationBus<Custom.RedisInvalidationBus>();
        }
    }

    /// <summary>Sincroniza a tabela de permissões com o código no startup (depois de mapear as rotas).</summary>
    public static async Task SincronizarPermissoesAsync(WebApplication app, bool somenteSimular)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        var siglas = app.Descobrir(typeof(ExemploComBanco).Assembly);   // controllers, Minimal API, policies, catálogos

        // Lança InvalidOperationException (e não grava nada) se desativaria mais da metade das permissões
        var resultado = await db.SincronizarAsync(siglas, opcoes: new SincronizacaoOpcoes
        {
            Simular = somenteSimular,   // true em CI/homologação: só calcula
            Logger = logger,
            Invalidador = scope.ServiceProvider.GetRequiredService<IPermissaoCacheInvalidator>()   // renova o cache após reativar/desativar
        });

        logger.LogInformation("Permissões: {Criadas} criadas, {Reativadas} reativadas, {Desativadas} desativadas",
            resultado.Criadas, resultado.Reativadas, resultado.Desativadas);
    }

    /// <summary>Ao bloquear um usuário ou trocar o perfil dele: grave e invalide o cache.</summary>
    public static async Task BloquearUsuarioAsync(AppDbContext db, IPermissaoCacheInvalidator cache, IPermissaoAuditor auditor,
        int usuarioId, string ator)
    {
        var usuario = await db.Usuarios.FindAsync(usuarioId) ?? throw new KeyNotFoundException();
        usuario.Ativo = false;
        await db.SaveChangesAsync();

        await cache.InvalidarUsuarioAsync(usuarioId);   // vale na hora (e nas outras instâncias, com o bus)
        await auditor.RegistrarAsync(new PermissaoAuditoriaEvento(
            PermissaoAuditoriaTipo.UsuarioBloqueado, ator, $"usuario {usuarioId}"));
    }
}
