using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.EntityFramework.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Interfaces;
using Xunit;
using Xunit.Abstractions;

namespace Prime.Architecture.Authorization.EntityFramework.MySqlTests;

/// <summary>Só roda com a variável PRIME_MYSQL (connection string) e apenas contra o banco prime_auth_test.</summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Env.ConnectionString))
            Skip = "Defina PRIME_MYSQL para rodar os testes contra MySQL.";
        else if (!Env.BancoSeguro)
            Skip = $"Recusado: o banco da PRIME_MYSQL deve ser '{Env.BancoPermitido}'.";
    }
}

internal static class Env
{
    public const string BancoPermitido = "prime_auth_test";
    public static string? ConnectionString => Environment.GetEnvironmentVariable("PRIME_MYSQL");

    public static bool BancoSeguro =>
        ConnectionString is { } c &&
        c.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Any(p => p.Length == 2 &&
                      (p[0].Trim().Equals("database", StringComparison.OrdinalIgnoreCase) ||
                       p[0].Trim().Equals("initial catalog", StringComparison.OrdinalIgnoreCase)) &&
                      p[1].Trim() == BancoPermitido);
}

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.AplicarResourceAuthorization();
}

public sealed class ContadorDeConsultas : DbCommandInterceptor
{
    private int _total;
    public int Total => _total;
    public void Zerar() => Interlocked.Exchange(ref _total, 0);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _total);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

[CollectionDefinition("mysql", DisableParallelization = true)]
public class MySqlCollection { }

[Collection("mysql")]
public class MySqlTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly ContadorDeConsultas _contador = new();
    private readonly ServiceProvider? _provider;

    public MySqlTests(ITestOutputHelper output)
    {
        _out = output;
        if (!Env.BancoSeguro) return;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseMySQL(Env.ConnectionString!).AddInterceptors(_contador));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext>(o =>
        {
            o.DuracaoCache = TimeSpan.FromMinutes(5);
            o.ConfiarNoPerfilDoToken = true;
        });
        _provider = services.BuildServiceProvider();

        // Banco dedicado: recria somente as 3 tabelas do pacote
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.ExecuteSqlRaw("SET FOREIGN_KEY_CHECKS=0");
        foreach (var t in new[] { "PerfisPermissoes", "UsuariosPermissoes", "Permissoes" })
            db.Database.ExecuteSqlRaw($"DROP TABLE IF EXISTS `{t}`");
        db.Database.ExecuteSqlRaw("SET FOREIGN_KEY_CHECKS=1");
        db.Database.EnsureCreated();
    }

    public void Dispose() => _provider?.Dispose();

    private static ClaimsPrincipal Usuario(int usuarioId, int perfilId)
        => new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, usuarioId.ToString()),
            new Claim("perfil_id", perfilId.ToString())
        }, "teste"));

    private async Task Executar(Func<AppDbContext, Task> acao)
    {
        using var scope = _provider!.CreateScope();
        await acao(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<IReadOnlySet<string>> Resolver(ClaimsPrincipal user)
    {
        using var scope = _provider!.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
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
                new PerfilPermissao { PerfilId = 2, PermissaoId = ids["produto:read"] });
            db.Set<UsuarioPermissao>().Add(new UsuarioPermissao { UsuarioId = 10, PermissaoId = ids["produto:delete"], Concedida = false });
            db.Set<UsuarioPermissao>().Add(new UsuarioPermissao { UsuarioId = 11, PermissaoId = ids["produto:create"] });
            await db.SaveChangesAsync();
        });
    }

    // ---------- Esquema ----------

    [MySqlFact]
    public async Task Esquema_tem_indices_unicos_e_fk()
    {
        await Executar(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT DISTINCT TABLE_NAME, INDEX_NAME, NON_UNIQUE FROM information_schema.STATISTICS
                                WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 AND INDEX_NAME <> 'PRIMARY'";
            var unicos = new List<string>();
            await using (var r = await cmd.ExecuteReaderAsync())
                while (await r.ReadAsync()) unicos.Add($"{r.GetString(0)}.{r.GetString(1)}");
            _out.WriteLine(string.Join(", ", unicos));
            Assert.Equal(3, unicos.Count);

            cmd.CommandText = "SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE()";
            Assert.Equal(2L, Convert.ToInt64(await cmd.ExecuteScalarAsync()));
        });
    }

    // ---------- Resolver ----------

    [MySqlFact]
    public async Task Resolver_combina_perfil_e_excecoes_de_usuario()
    {
        await Semear();
        Assert.Equal(new[] { "produto:delete", "produto:read" }, (await Resolver(Usuario(1, 1))).OrderBy(x => x));
        // negação explícita vence o perfil
        Assert.Equal(new[] { "produto:read" }, (await Resolver(Usuario(10, 1))).OrderBy(x => x));
        // concessão individual acrescenta
        Assert.Equal(new[] { "produto:create", "produto:read" }, (await Resolver(Usuario(11, 2))).OrderBy(x => x));
        // perfil inexistente / claims ausentes
        Assert.Empty(await Resolver(Usuario(99, 999)));
        Assert.Empty(await Resolver(new ClaimsPrincipal(new ClaimsIdentity("teste"))));
    }

    [MySqlFact]
    public async Task Permissao_inativa_nao_vale()
    {
        await Semear();
        await Executar(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE Permissoes SET Ativo = 0 WHERE Sigla = 'produto:delete'");
        });
        Assert.Equal(new[] { "produto:read" }, (await Resolver(Usuario(1, 1))).OrderBy(x => x));
    }

    [MySqlFact]
    public async Task Sigla_hostil_e_gravada_literalmente_e_nao_executa_sql()
    {
        var hostil = "x'; DROP TABLE Permissoes; --";
        await Executar(db => db.SincronizarAsync(new[] { hostil }));
        await Executar(async db =>
        {
            Assert.Equal(hostil.ToLowerInvariant(), (await db.Set<Permissao>().SingleAsync()).Sigla);
        });
    }

    // ---------- Collation (utf8mb4_0900_ai_ci: insensível a caixa E a acento) ----------

    [MySqlFact]
    public async Task Indice_unico_ignora_caixa_e_acento()
    {
        await Executar(db => db.SincronizarAsync(new[] { "acao:ler" }));

        // Mesma sigla com outra caixa: o banco rejeita (índice único insensível a caixa)
        await Executar(async db =>
        {
            db.Set<Permissao>().Add(new Permissao { Sigla = "ACAO:LER", Descricao = "x" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        });

        // Com acento: o código ('ordinal') vê duas siglas diferentes, o banco vê a mesma
        // ACHADO: a sincronização inteira falha (DbUpdateException) em vez de tratar as duas como a mesma sigla
        var ex = await Record.ExceptionAsync(() => Executar(db => db.SincronizarAsync(new[] { "acao:ler", "ação:ler" })));
        _out.WriteLine("Sincronizar 'acao:ler' + 'ação:ler' => " + (ex == null ? "OK" : ex.GetType().Name + ": " + (ex.InnerException?.Message ?? ex.Message)));
        Assert.IsType<DbUpdateException>(ex);
    }

    [MySqlFact]
    public async Task Sigla_acima_de_150_caracteres_e_rejeitada_sem_truncar()
    {
        var longa = "r:" + new string('a', 200);
        var ex = await Record.ExceptionAsync(() => Executar(db => db.SincronizarAsync(new[] { longa })));
        _out.WriteLine("Sigla longa => " + (ex == null ? "aceita" : ex.GetType().Name));
        Assert.NotNull(ex);
        await Executar(async db => Assert.Empty(await db.Set<Permissao>().ToListAsync()));
    }

    [MySqlFact]
    public async Task Utf8mb4_ida_e_volta()
    {
        await Executar(db => db.SincronizarAsync(new[] { "relatório:exportar", "emoji:😀" }));
        await Executar(async db =>
        {
            var siglas = (await db.Set<Permissao>().ToListAsync()).Select(p => p.Sigla).OrderBy(x => x).ToList();
            Assert.Contains("emoji:😀", siglas);
            Assert.Contains("relatório:exportar", siglas);
        });
    }

    // ---------- Sincronização ----------

    [MySqlFact]
    public async Task Sincronizacao_desativa_orfas_respeitando_limite()
    {
        await Executar(db => db.SincronizarAsync(new[] { "a:1", "b:1", "c:1", "d:1" }));
        await Executar(async db =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SincronizarAsync(new[] { "a:1" }));
        });
        await Executar(async db =>
        {
            var r = await db.SincronizarAsync(new[] { "a:1", "b:1", "c:1" });
            Assert.Equal(1, r.Desativadas);
        });
        await Executar(async db => Assert.Equal(3, await db.Set<Permissao>().CountAsync(p => p.Ativo)));
    }

    [MySqlFact]
    public async Task Duas_instancias_sincronizando_ao_mesmo_tempo()
    {
        var siglas = Enumerable.Range(0, 300).Select(i => $"conc:{i}").ToArray();
        var tarefas = Enumerable.Range(0, 8).Select(_ => Task.Run(() => Record.ExceptionAsync(() => Executar(db => db.SincronizarAsync(siglas))))).ToArray();
        var erros = (await Task.WhenAll(tarefas)).Where(e => e != null).ToList();
        _out.WriteLine($"8 sincronizações simultâneas: {erros.Count} falharam. " + string.Join(" | ", erros.Select(e => e!.GetType().Name)));

        // Seja qual for o resultado, o banco não pode ficar inconsistente nem com duplicatas
        await Executar(async db => Assert.Equal(300, await db.Set<Permissao>().CountAsync()));
        // Uma nova execução deve convergir
        await Executar(db => db.SincronizarAsync(siglas));
        Assert.True(erros.Count == 0, "Sincronização concorrente deveria ser tolerada (releitura e nova tentativa).");
    }

    [MySqlFact]
    public async Task Sincronizacao_com_invalidador_atualiza_o_cache_do_resolver()
    {
        await Semear();
        Assert.Contains("produto:delete", await Resolver(Usuario(1, 1)));      // aquece o cache
        await Executar(db => db.SincronizarAsync(new[] { "produto:read", "produto:create" },
            opcoes: new SincronizacaoOpcoes { Invalidador = _provider!.GetRequiredService<IPermissaoCacheInvalidator>() }));
        Assert.DoesNotContain("produto:delete", await Resolver(Usuario(1, 1)));
    }

    [MySqlFact]
    public async Task Sincronizacao_sem_invalidador_mantem_o_cache_ate_expirar()
    {
        await Semear();
        Assert.Contains("produto:delete", await Resolver(Usuario(1, 1)));
        await Executar(db => db.SincronizarAsync(new[] { "produto:read", "produto:create" }));
        Assert.Contains("produto:delete", await Resolver(Usuario(1, 1)));   // comportamento documentado
    }

    // ---------- Cache / carga contra banco remoto ----------

    [MySqlFact]
    public async Task Stampede_com_cache_frio_faz_uma_consulta_por_chave()
    {
        await Semear();
        _contador.Zerar();
        var user = Usuario(1, 1);
        var resultados = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => Resolver(user))));
        Assert.All(resultados, r => Assert.Equal(2, r.Count));
        _out.WriteLine($"200 requisições simultâneas, cache frio => {_contador.Total} consultas");
        Assert.Equal(2, _contador.Total);   // 1 do perfil + 1 das exceções do usuário
    }

    [MySqlFact]
    public async Task Latencia_com_e_sem_cache()
    {
        await Semear();
        var user = Usuario(1, 1);
        await Resolver(user);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 2000; i++) await Resolver(user);
        _out.WriteLine($"Com cache: {sw.Elapsed.TotalMilliseconds / 2000:F3} ms/chamada");

        // Sem cache: resolver direto com DuracaoCache zero
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseMySQL(Env.ConnectionString!));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddResourceAuthorizationEntityFramework<AppDbContext>(o => { o.DuracaoCache = TimeSpan.Zero; o.ConfiarNoPerfilDoToken = true; });
        await using var sp = services.BuildServiceProvider();
        sw.Restart();
        for (var i = 0; i < 30; i++)
        {
            using var scope = sp.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IPermissaoResolver>().ObterAsync(user);
        }
        _out.WriteLine($"Sem cache: {sw.Elapsed.TotalMilliseconds / 30:F1} ms/chamada (banco remoto)");
    }
}
