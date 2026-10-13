using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prime.Architecture.Authorization.EntityFramework;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Interfaces;
using Xunit;

namespace Prime.Architecture.Authorization.SecurityTests;

public class AttackDbContext(DbContextOptions<AttackDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AplicarResourceAuthorization();
}

/// <summary>"Banco de usuários" da aplicação: só o que está aqui existe e está ativo.</summary>
public class UsuariosDoBanco : IUsuarioPerfilProvider
{
    public static readonly Dictionary<int, UsuarioPerfilInfo> Ativos = new();

    public Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Task.FromResult<UsuarioPerfilInfo?>(null);
        return Task.FromResult(Ativos.GetValueOrDefault(id));
    }
}

public class EfAttackTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public EfAttackTests()
    {
        UsuariosDoBanco.Ativos.Clear();
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AttackDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AttackDbContext, UsuariosDoBanco>();
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttackDbContext>();
        db.Database.EnsureCreated();
        db.SincronizarAsync(new[] { "produto:read", "produto:delete", "admin:manage" }).GetAwaiter().GetResult();
        var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);
        db.Set<PerfilPermissao>().AddRange(
            new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] },
            new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:delete"] },
            new PerfilPermissao { PerfilId = 1, PermissaoId = ids["admin:manage"] },
            new PerfilPermissao { PerfilId = 2, PermissaoId = ids["produto:read"] });
        db.SaveChanges();

        UsuariosDoBanco.Ativos[10] = new UsuarioPerfilInfo(10, 2); // usuário comum, só leitura
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private async Task<IReadOnlySet<string>> Resolver(params Claim[] claims)
    {
        using var scope = _provider.CreateScope();
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "teste"));
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
    }

    [Fact]
    public async Task Perfil_forjado_no_token_e_ignorado_quando_o_perfil_vem_do_banco()
    {
        // O token afirma perfil admin (1); o banco diz que o usuário 10 é perfil 2
        var perms = await Resolver(new Claim(ClaimTypes.NameIdentifier, "10"), new Claim("perfil_id", "1"));
        Assert.Equal(new[] { "produto:read" }, perms);
    }

    [Fact]
    public async Task Permissoes_forjadas_no_token_sao_ignoradas_pelo_resolver_do_banco()
    {
        var perms = await Resolver(new Claim(ClaimTypes.NameIdentifier, "10"), new Claim("permissao", "admin:manage"));
        Assert.DoesNotContain("admin:manage", perms);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("99999999999999999999")]
    [InlineData("1; DROP TABLE Permissoes")]
    [InlineData("1 OR 1=1")]
    [InlineData("abc")]
    [InlineData("")]
    public async Task Ids_hostis_nas_claims_nao_dao_permissao_nem_quebram(string id)
    {
        var perms = await Resolver(new Claim(ClaimTypes.NameIdentifier, id), new Claim("perfil_id", id));
        Assert.Empty(perms);
    }

    [Fact]
    public async Task Usuario_inexistente_ou_removido_fica_sem_permissao()
    {
        UsuariosDoBanco.Ativos[11] = new UsuarioPerfilInfo(11, 1);
        Assert.Contains("admin:manage", await Resolver(new Claim(ClaimTypes.NameIdentifier, "11")));

        UsuariosDoBanco.Ativos.Remove(11); // bloqueado no banco
        await _provider.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarUsuarioAsync(11); // regra: invalidar ao bloquear
        Assert.Empty(await Resolver(new Claim(ClaimTypes.NameIdentifier, "11")));
    }

    [Fact]
    public async Task Siglas_maliciosas_na_sincronizacao_sao_tratadas_como_texto()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttackDbContext>();

        var maliciosas = new[]
        {
            "x'; DROP TABLE Permissoes; --",
            "\" OR 1=1 --",
            "a:b'); DELETE FROM PerfisPermissoes; --"
        };
        var todas = db.Set<Permissao>().Select(p => p.Sigla).ToList().Concat(maliciosas);
        await db.SincronizarAsync(todas);

        // Tabelas e vínculos intactos; as siglas viraram linhas comuns
        Assert.Equal(6, db.Set<Permissao>().Count());
        Assert.Equal(4, db.Set<PerfilPermissao>().Count());
        Assert.Contains(db.Set<Permissao>().Select(p => p.Sigla).ToList(), s => s.StartsWith("x';"));
    }

    [Fact]
    public async Task Sincronizacao_com_lista_vazia_ou_lixo_nao_derruba_o_sistema()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AttackDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SincronizarAsync(Array.Empty<string>()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SincronizarAsync(new[] { "   ", "" }));
        Assert.Equal(3, db.Set<Permissao>().Count(p => p.Ativo));

        // E os usuários continuam com acesso
        UsuariosDoBanco.Ativos[12] = new UsuarioPerfilInfo(12, 1);
        Assert.Contains("admin:manage", await Resolver(new Claim(ClaimTypes.NameIdentifier, "12")));
    }

    [Fact]
    public async Task Cache_de_um_usuario_nao_vaza_para_outro()
    {
        UsuariosDoBanco.Ativos[20] = new UsuarioPerfilInfo(20, 1);
        UsuariosDoBanco.Ativos[21] = new UsuarioPerfilInfo(21, 2);

        var admin = await Resolver(new Claim(ClaimTypes.NameIdentifier, "20"));
        var comum = await Resolver(new Claim(ClaimTypes.NameIdentifier, "21"));
        var adminDeNovo = await Resolver(new Claim(ClaimTypes.NameIdentifier, "20"));

        Assert.Contains("admin:manage", admin);
        Assert.DoesNotContain("admin:manage", comum);
        Assert.Contains("admin:manage", adminDeNovo);
    }

    [Fact]
    public void Registro_inseguro_exige_opt_in_explicito()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddResourceAuthorizationEntityFramework<AttackDbContext>());
        Assert.Throws<InvalidOperationException>(() => services.AddResourceAuthorizationEntityFramework<AttackDbContext>(o => o.DuracaoCache = TimeSpan.Zero));
    }
}
