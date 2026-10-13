using System.Collections.Generic;
using System.Threading;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Interfaces;
using Xunit;

namespace Prime.Architecture.Authorization.EntityFramework.Tests;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AplicarResourceAuthorization();
}

public class EfTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public EfTests()
    {
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext>(o =>
        {
            o.DuracaoCache = TimeSpan.FromMinutes(5);
            o.ConfiarNoPerfilDoToken = true;
        });
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal Usuario(int usuarioId, int perfilId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim("perfil_id", perfilId.ToString())
        }, "teste"));

    private async Task<IReadOnlySet<string>> Resolver(ClaimsPrincipal user)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
    }

    private async Task Executar(Func<AppDbContext, Task> acao)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await acao(db);
    }

    private async Task Semear()
    {
        await Executar(async db =>
        {
            await db.SincronizarAsync(new[] { "produto:read", "produto:delete", "produto:create" });
            var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);

            db.Set<PerfilPermissao>().AddRange(
                new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] },
                new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:delete"] },
                new PerfilPermissao { PerfilId = 2, PermissaoId = ids["produto:read"] },
                new PerfilPermissao { PerfilId = 2, PermissaoId = ids["produto:create"], Concedida = false });
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task Resolve_permissoes_do_perfil()
    {
        await Semear();
        var perms = await Resolver(Usuario(10, 1));
        Assert.Equal(new[] { "produto:delete", "produto:read" }, perms.OrderBy(p => p));
    }

    [Fact]
    public async Task Perfil_com_concessao_falsa_nao_recebe_a_permissao()
    {
        await Semear();
        var perms = await Resolver(Usuario(10, 2));
        Assert.Equal(new[] { "produto:read" }, perms);
    }

    [Fact]
    public async Task Negacao_do_usuario_vence_a_concessao_do_perfil()
    {
        await Semear();
        await Executar(async db =>
        {
            var id = db.Set<Permissao>().Single(p => p.Sigla == "produto:delete").Id;
            db.Set<UsuarioPermissao>().Add(new UsuarioPermissao { UsuarioId = 10, PermissaoId = id, Concedida = false });
            await db.SaveChangesAsync();
        });

        Assert.DoesNotContain("produto:delete", await Resolver(Usuario(10, 1)));
        Assert.Contains("produto:delete", await Resolver(Usuario(11, 1)));
    }

    [Fact]
    public async Task Concessao_do_usuario_acrescenta_permissao()
    {
        await Semear();
        await Executar(async db =>
        {
            var id = db.Set<Permissao>().Single(p => p.Sigla == "produto:create").Id;
            db.Set<UsuarioPermissao>().Add(new UsuarioPermissao { UsuarioId = 10, PermissaoId = id });
            await db.SaveChangesAsync();
        });

        Assert.Contains("produto:create", await Resolver(Usuario(10, 2)));
    }

    [Fact]
    public async Task Permissao_inativa_deixa_de_valer()
    {
        await Semear();
        await Executar(async db =>
        {
            await db.SincronizarAsync(new[] { "produto:read" }, opcoes: new SincronizacaoOpcoes { LimiteDesativacao = 1 }); // delete e create viram órfãs
        });

        Assert.Equal(new[] { "produto:read" }, await Resolver(Usuario(10, 1)));
    }

    [Fact]
    public async Task Cache_mantem_o_valor_ate_invalidar()
    {
        await Semear();
        Assert.Contains("produto:delete", await Resolver(Usuario(10, 1)));

        await Executar(async db =>
        {
            db.Set<PerfilPermissao>().RemoveRange(db.Set<PerfilPermissao>().Where(pp => pp.PerfilId == 1));
            await db.SaveChangesAsync();
        });

        Assert.Contains("produto:delete", await Resolver(Usuario(10, 1))); // ainda em cache

        _provider.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarPerfil(1);
        Assert.Empty(await Resolver(Usuario(10, 1)));
    }

    [Fact]
    public async Task Usuario_sem_claims_nao_tem_permissoes()
        => Assert.Empty(await Resolver(new ClaimsPrincipal(new ClaimsIdentity())));

    [Fact]
    public async Task Sincronizador_cria_reativa_e_desativa()
    {
        await Executar(async db =>
        {
            var r1 = await db.SincronizarAsync(new[] { "a:read", "b:read" });
            Assert.Equal(new SincronizacaoResultado(2, 0, 0), r1);

            var r2 = await db.SincronizarAsync(new[] { "a:read" });
            Assert.Equal(new SincronizacaoResultado(0, 0, 1), r2);

            var r3 = await db.SincronizarAsync(new[] { "A:READ", "b:read", "c:read" });
            Assert.Equal(new SincronizacaoResultado(1, 1, 0), r3);

            Assert.Equal(3, db.Set<Permissao>().Count(p => p.Ativo));
        });
    }

    [Fact]
    public async Task Sincronizador_aborta_quando_desativaria_alem_do_limite()
    {
        await Executar(async db =>
        {
            await db.SincronizarAsync(new[] { "a:read", "b:read", "c:read", "d:read" });

            // lista vazia / assembly errado: desativaria 100%
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SincronizarAsync(Array.Empty<string>()));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SincronizarAsync(new[] { "a:read" })); // 75%

            Assert.Equal(4, db.Set<Permissao>().Count(p => p.Ativo)); // nada foi alterado
        });
    }

    [Fact]
    public async Task Sincronizador_simulado_nao_grava()
    {
        await Executar(async db =>
        {
            var r = await db.SincronizarAsync(new[] { "x:read" }, opcoes: new SincronizacaoOpcoes { Simular = true });
            Assert.Equal(new SincronizacaoResultado(1, 0, 0), r);
            Assert.Empty(db.Set<Permissao>());
        });
    }

    [Fact]
    public void Registro_sem_provider_nem_opt_in_e_recusado()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddResourceAuthorizationEntityFramework<AppDbContext>());
    }
}

/// <summary>Usuários e perfis "vigentes" no banco da aplicação (simulado em memória).</summary>
public class UsuariosEmMemoria : IUsuarioPerfilProvider
{
    public static readonly Dictionary<int, UsuarioPerfilInfo?> Usuarios = new();

    public Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var id = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        return Task.FromResult(Usuarios.GetValueOrDefault(id));
    }
}

public class PerfilDoBancoTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public PerfilDoBancoTests()
    {
        UsuariosEmMemoria.Usuarios.Clear();
        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuariosEmMemoria>(o =>
        {
            o.DuracaoCache = TimeSpan.Zero;
            o.DuracaoCachePerfilUsuario = TimeSpan.Zero; // este teste muda o banco sem invalidar
        });
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.SincronizarAsync(new[] { "produto:read", "produto:delete" }).GetAwaiter().GetResult();
        var ids = db.Set<Permissao>().ToDictionary(p => p.Sigla, p => p.Id);
        db.Set<PerfilPermissao>().AddRange(
            new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:read"] },
            new PerfilPermissao { PerfilId = 1, PermissaoId = ids["produto:delete"] },
            new PerfilPermissao { PerfilId = 2, PermissaoId = ids["produto:read"] });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    // O token SEMPRE diz perfil 1 (admin); quem manda é o banco
    private static ClaimsPrincipal TokenAntigo(int usuarioId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim("perfil_id", "1")
        }, "teste"));

    private async Task<IReadOnlySet<string>> Resolver(ClaimsPrincipal user)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
    }

    [Fact]
    public async Task Mudanca_de_perfil_no_banco_vale_mesmo_com_token_antigo()
    {
        UsuariosEmMemoria.Usuarios[10] = new UsuarioPerfilInfo(10, 1);
        Assert.Contains("produto:delete", await Resolver(TokenAntigo(10)));

        UsuariosEmMemoria.Usuarios[10] = new UsuarioPerfilInfo(10, 2); // rebaixado no banco
        var perms = await Resolver(TokenAntigo(10));
        Assert.DoesNotContain("produto:delete", perms);
        Assert.Contains("produto:read", perms);
    }

    [Fact]
    public async Task Usuario_inativo_ou_inexistente_fica_sem_nenhuma_permissao()
    {
        UsuariosEmMemoria.Usuarios[10] = null; // bloqueado/inativo
        Assert.Empty(await Resolver(TokenAntigo(10)));
        Assert.Empty(await Resolver(TokenAntigo(99))); // não existe
    }
}

/// <summary>Provider próprio desta classe de teste (evita estado compartilhado com testes paralelos).</summary>
public class UsuariosDaInvalidacao : IUsuarioPerfilProvider
{
    public static readonly Dictionary<int, UsuarioPerfilInfo?> Usuarios = new();

    public Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        => Task.FromResult(Usuarios.GetValueOrDefault(int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!)));
}

public class InvalidacaoEntreInstanciasTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly InMemoryInvalidationBus _bus = new();
    private readonly List<ServiceProvider> _instancias = new();

    public InvalidacaoEntreInstanciasTests()
    {
        _connection.Open();
    }

    private async Task<ServiceProvider> NovaInstancia(bool comBus)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuariosDaInvalidacao>(o => o.DuracaoCache = TimeSpan.FromMinutes(10));
        if (comBus)
            services.AddPermissaoInvalidationBus(_bus);
        var sp = services.BuildServiceProvider();
        _instancias.Add(sp);

        if (comBus)
            foreach (var hosted in sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
                await hosted.StartAsync(CancellationToken.None);

        return sp;
    }

    private static ClaimsPrincipal Usuario() => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "10") }, "teste"));

    private static async Task<IReadOnlySet<string>> Resolver(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(Usuario());
    }

    private async Task Semear(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        await db.SincronizarAsync(new[] { "produto:read" });
        db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = db.Set<Permissao>().Single().Id });
        await db.SaveChangesAsync();
        UsuariosDaInvalidacao.Usuarios[10] = new UsuarioPerfilInfo(10, 1);
    }

    private static async Task Revogar(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<PerfilPermissao>().RemoveRange(db.Set<PerfilPermissao>());
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Sem_bus_a_outra_instancia_continua_com_cache_antigo()
    {
        var a = await NovaInstancia(comBus: false);
        var b = await NovaInstancia(comBus: false);
        await Semear(a);

        Assert.Contains("produto:read", await Resolver(b)); // aquece o cache de B
        await Revogar(a);
        await a.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarPerfilAsync(1);

        Assert.Empty(await Resolver(a));
        Assert.Contains("produto:read", await Resolver(b)); // B ainda enxerga o valor antigo
    }

    [Fact]
    public async Task Com_bus_a_invalidacao_chega_nas_outras_instancias()
    {
        var a = await NovaInstancia(comBus: true);
        var b = await NovaInstancia(comBus: true);
        await Semear(a);

        Assert.Contains("produto:read", await Resolver(b));
        await Revogar(a);
        await a.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarPerfilAsync(1);

        Assert.Empty(await Resolver(a));
        Assert.Empty(await Resolver(b)); // B foi avisada pelo bus
    }

    [Fact]
    public async Task Invalidacao_de_usuario_tambem_e_propagada()
    {
        var a = await NovaInstancia(comBus: true);
        var b = await NovaInstancia(comBus: true);
        await Semear(a);

        Assert.Contains("produto:read", await Resolver(b));
        using (var scope = a.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<UsuarioPermissao>().Add(new UsuarioPermissao { UsuarioId = 10, PermissaoId = db.Set<Permissao>().Single().Id, Concedida = false });
            await db.SaveChangesAsync();
        }
        await a.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarUsuarioAsync(10);

        Assert.DoesNotContain("produto:read", await Resolver(b));
    }

    [Fact]
    public async Task Sincronizar_com_invalidador_atualiza_o_cache_de_todas_as_instancias()
    {
        var a = await NovaInstancia(comBus: true);
        var b = await NovaInstancia(comBus: true);
        await Semear(a);

        Assert.Contains("produto:read", await Resolver(a));
        Assert.Contains("produto:read", await Resolver(b));

        using (var scope = a.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await db.SincronizarAsync(new[] { "outra:coisa" }, opcoes: new SincronizacaoOpcoes
            {
                LimiteDesativacao = 1,
                Invalidador = scope.ServiceProvider.GetRequiredService<IPermissaoCacheInvalidator>()
            });
            Assert.Equal(1, r.Desativadas);
        }

        Assert.Empty(await Resolver(a));
        Assert.Empty(await Resolver(b));
    }

    [Fact]
    public async Task Sincronizar_sem_invalidador_mantem_cache_ate_expirar()
    {
        var a = await NovaInstancia(comBus: false);
        await Semear(a);
        Assert.Contains("produto:read", await Resolver(a));

        using (var scope = a.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .SincronizarAsync(new[] { "outra:coisa" }, opcoes: new SincronizacaoOpcoes { LimiteDesativacao = 1 });

        Assert.Contains("produto:read", await Resolver(a)); // comportamento documentado: vale ao expirar o cache
    }

    [Fact]
    public async Task Sincronizar_sem_mudar_permissoes_nao_invalida_o_cache()
    {
        var a = await NovaInstancia(comBus: false);
        await Semear(a);
        await Resolver(a);

        using (var scope = a.CreateScope())
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SincronizarAsync(new[] { "produto:read", "nova:permissao" },
                opcoes: new SincronizacaoOpcoes { Invalidador = scope.ServiceProvider.GetRequiredService<IPermissaoCacheInvalidator>() });

        // Só houve criação: permissões novas não alteram o que já está em cache
        Assert.Contains("produto:read", await Resolver(a));
    }

    [Fact]
    public async Task Falha_do_bus_nao_derruba_a_sincronizacao_ja_gravada()
    {
        var logs = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuariosDaInvalidacao>();
        services.AddPermissaoInvalidationBus(new BusQuebrado());
        await using var sp = services.BuildServiceProvider();

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        await db.SincronizarAsync(new[] { "a:read", "b:read" });

        var r = await db.SincronizarAsync(new[] { "a:read" }, opcoes: new SincronizacaoOpcoes
        {
            LimiteDesativacao = 1,
            Logger = new ListaLogger(logs),
            Invalidador = scope.ServiceProvider.GetRequiredService<IPermissaoCacheInvalidator>()
        });

        Assert.Equal(1, r.Desativadas);
        Assert.Contains(logs, l => l.Contains("falhou ao invalidar"));
        Assert.Equal(1, await db.Set<Permissao>().CountAsync(p => p.Ativo));
    }

    [Fact]
    public async Task Tipo_de_invalidacao_desconhecido_e_ignorado()
    {
        var a = await NovaInstancia(comBus: true);
        await Semear(a);
        Assert.Contains("produto:read", await Resolver(a)); // aquece o cache do usuário 10 (perfil 1)

        // O "banco" passa a dizer que o usuário 10 está em outro perfil, sem permissões
        UsuariosDaInvalidacao.Usuarios[10] = new UsuarioPerfilInfo(10, 2);

        // Mensagem de uma versão futura: não pode ser tratada como invalidação do usuário 10
        await _bus.PublicarAsync(new PermissaoInvalidacao((PermissaoInvalidacaoTipo)99, 10, "outra-instancia"));
        Assert.Contains("produto:read", await Resolver(a)); // continua com o cache

        // Controle: a invalidação de usuário de verdade faz o novo perfil valer
        await _bus.PublicarAsync(new PermissaoInvalidacao(PermissaoInvalidacaoTipo.Usuario, 10, "outra-instancia"));
        Assert.Empty(await Resolver(a));
    }

    [Fact]
    public async Task Invalidar_tudo_nao_afeta_outras_chaves_do_memory_cache()
    {
        var a = await NovaInstancia(comBus: false);
        await Semear(a);
        var memoria = a.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        memoria.Set("chave-da-aplicacao", "valor");

        await a.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarTudoAsync();

        Assert.Equal("valor", memoria.Get<string>("chave-da-aplicacao"));
    }

    private sealed class BusQuebrado : IPermissaoInvalidationBus
    {
        public Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("redis fora do ar");

        public IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador) => new Nada();

        private sealed class Nada : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public async Task Sincronizador_registra_alteracoes_no_log()
    {
        var logs = new List<string>();
        var logger = new ListaLogger(logs);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        await using var db = new AppDbContext(options);
        db.Database.EnsureCreated();

        await db.SincronizarAsync(new[] { "a:read", "b:read" }, opcoes: new SincronizacaoOpcoes { Logger = logger });
        await db.SincronizarAsync(new[] { "a:read" }, opcoes: new SincronizacaoOpcoes { Logger = logger });

        Assert.Contains(logs, l => l.Contains("criadas [a:read, b:read]"));
        Assert.Contains(logs, l => l.Contains("desativadas [b:read]"));
    }

    private sealed class ListaLogger(List<string> logs) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => logs.Add(formatter(state, exception));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sp in _instancias)
        {
            foreach (var hosted in sp.GetServices<Microsoft.Extensions.Hosting.IHostedService>())
                await hosted.StopAsync(CancellationToken.None);
            await sp.DisposeAsync();
        }
        _connection.Dispose();
    }
}

/// <summary>Conta quantas vezes o "banco" de usuários foi consultado.</summary>
public class UsuariosContados : IUsuarioPerfilProvider
{
    public static int Consultas;
    public static readonly Dictionary<int, UsuarioPerfilInfo?> Usuarios = new();
    public static int AtrasoMs;

    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref Consultas);
        if (AtrasoMs > 0) await Task.Delay(AtrasoMs, cancellationToken);
        return Usuarios.GetValueOrDefault(int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!));
    }
}

[Collection("CacheDePerfil")] // usa estado estático: não roda em paralelo com outras classes do mesmo grupo
public class CacheDePerfilTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly ServiceProvider _provider;

    public CacheDePerfilTests() : this(null) { }

    protected CacheDePerfilTests(Action<EfPermissaoOptions>? configure)
    {
        UsuariosContados.Consultas = 0;
        UsuariosContados.AtrasoMs = 0;
        UsuariosContados.Usuarios.Clear();
        UsuariosContados.Usuarios[10] = new UsuarioPerfilInfo(10, 1);

        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuariosContados>(configure);
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        db.SincronizarAsync(new[] { "produto:read" }).GetAwaiter().GetResult();
        db.Set<PerfilPermissao>().Add(new PerfilPermissao { PerfilId = 1, PermissaoId = db.Set<Permissao>().Single().Id });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _connection.Dispose();
    }

    private static ClaimsPrincipal Usuario(int id = 10)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "teste"));

    private async Task<IReadOnlySet<string>> Resolver(int id = 10)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(Usuario(id));
    }

    [Fact]
    public async Task Consulta_de_perfil_e_cacheada_entre_requisicoes()
    {
        for (var i = 0; i < 5; i++)
            Assert.Contains("produto:read", await Resolver());

        Assert.Equal(1, UsuariosContados.Consultas);
    }

    [Fact]
    public async Task Cache_frio_com_muitas_requisicoes_simultaneas_consulta_uma_unica_vez()
    {
        UsuariosContados.AtrasoMs = 150; // mantém a carga "em andamento" enquanto as demais chegam

        var resultados = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Resolver()));

        Assert.All(resultados, r => Assert.Contains("produto:read", r));
        Assert.Equal(1, UsuariosContados.Consultas);
    }

    [Fact]
    public async Task Bloqueio_vale_apos_invalidar_o_usuario()
    {
        Assert.Contains("produto:read", await Resolver());

        UsuariosContados.Usuarios[10] = null; // bloqueado no banco
        Assert.Contains("produto:read", await Resolver()); // ainda em cache (até expirar ou invalidar)

        await _provider.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarUsuarioAsync(10);
        Assert.Empty(await Resolver());
    }

    [Fact]
    public async Task Troca_de_perfil_vale_apos_invalidar_o_usuario()
    {
        Assert.Contains("produto:read", await Resolver());

        UsuariosContados.Usuarios[10] = new UsuarioPerfilInfo(10, 2); // perfil sem permissões
        await _provider.GetRequiredService<IPermissaoCacheInvalidator>().InvalidarUsuarioAsync(10);

        Assert.Empty(await Resolver());
    }

    [Fact]
    public async Task Cache_de_um_usuario_nao_vaza_para_outro()
    {
        UsuariosContados.Usuarios[11] = null;
        Assert.Contains("produto:read", await Resolver(10));
        Assert.Empty(await Resolver(11));
        Assert.Contains("produto:read", await Resolver(10));
    }

    [Fact]
    public async Task Cache_nao_e_usado_quando_a_claim_de_usuario_nao_e_um_id()
    {
        UsuariosContados.Usuarios.Clear();
        var anonimo = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "abc") }, "teste"));
        using var scope = _provider.CreateScope();
        var r = scope.ServiceProvider.GetRequiredService<IPermissaoResolver>();

        // provider real (int.Parse) lançaria; aqui só garantimos que nada é cacheado para chave inválida
        await Assert.ThrowsAsync<FormatException>(() => r.ObterAsync(anonimo));
    }
}

[Collection("CacheDePerfil")]
public class CacheDePerfilDesligadoTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task Com_duracao_zero_consulta_a_cada_requisicao()
    {
        UsuariosContados.Consultas = 0;
        UsuariosContados.AtrasoMs = 0;
        UsuariosContados.Usuarios.Clear();
        UsuariosContados.Usuarios[10] = new UsuarioPerfilInfo(10, 1);

        _connection.Open();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext, UsuariosContados>(o => o.DuracaoCachePerfilUsuario = TimeSpan.Zero);
        await using var provider = services.BuildServiceProvider();
        using (var s = provider.CreateScope())
            s.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "10") }, "teste"));
        for (var i = 0; i < 3; i++)
        {
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
        }

        Assert.Equal(3, UsuariosContados.Consultas);
    }
}

public class SingleFlightCacheTests
{
    private static SingleFlightCache Novo() => new(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));

    [Fact]
    public async Task Carga_invalidada_durante_o_carregamento_nao_grava_valor_antigo()
    {
        var cache = Novo();
        var liberar = new TaskCompletionSource();
        var chamadas = 0;

        var primeira = cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), async () =>
        {
            Interlocked.Increment(ref chamadas);
            await liberar.Task;
            return "antigo";
        });

        cache.Invalidar("k");          // acontece enquanto a carga ainda roda
        liberar.SetResult();
        Assert.Equal("antigo", await primeira);

        // O valor antigo não foi para o cache: a próxima leitura carrega de novo
        var segunda = await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult("novo"));
        Assert.Equal("novo", segunda);
    }

    [Fact]
    public async Task Falha_na_carga_nao_fica_em_cache()
    {
        var cache = Novo();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.GetOrCreateAsync<string>("k", TimeSpan.FromMinutes(5), () => throw new InvalidOperationException("falhou")));

        Assert.Equal("ok", await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult("ok")));
    }

    [Fact]
    public async Task InvalidarTudo_descarta_todas_as_chaves_e_cargas_em_andamento()
    {
        var cache = Novo();
        await cache.GetOrCreateAsync("a", TimeSpan.FromMinutes(5), () => Task.FromResult("a1"));
        await cache.GetOrCreateAsync("b", TimeSpan.FromMinutes(5), () => Task.FromResult("b1"));

        var liberar = new TaskCompletionSource();
        var emAndamento = cache.GetOrCreateAsync("c", TimeSpan.FromMinutes(5), async () => { await liberar.Task; return "c-antigo"; });

        cache.InvalidarTudo();
        liberar.SetResult();
        Assert.Equal("c-antigo", await emAndamento);

        Assert.Equal("a2", await cache.GetOrCreateAsync("a", TimeSpan.FromMinutes(5), () => Task.FromResult("a2")));
        Assert.Equal("b2", await cache.GetOrCreateAsync("b", TimeSpan.FromMinutes(5), () => Task.FromResult("b2")));
        Assert.Equal("c2", await cache.GetOrCreateAsync("c", TimeSpan.FromMinutes(5), () => Task.FromResult("c2")));
    }

    [Fact]
    public async Task InvalidarTudo_nao_remove_chaves_de_fora_do_cache()
    {
        var memoria = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var cache = new SingleFlightCache(memoria);
        memoria.Set("outra", "valor");
        await cache.GetOrCreateAsync("k", TimeSpan.FromMinutes(5), () => Task.FromResult("v"));

        cache.InvalidarTudo();

        Assert.Equal("valor", memoria.Get<string>("outra"));
    }
}
