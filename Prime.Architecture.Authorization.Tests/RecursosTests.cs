using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.Auditing;
using Prime.Architecture.Authorization.Diagnostics;
using Prime.Architecture.Authorization.Discovery;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Security;
using Xunit;

namespace Prime.Architecture.Authorization.Tests;

[CatalogoDePermissoes]
public static class PermissoesDeExemplo
{
    public const string RelatorioExportar = "Relatorio:Exportar";

    public static class Pedido
    {
        public const string Ler = "pedido:read";
        public const string Cancelar = "pedido:cancel";
    }
}

[ApiController, Route("api/recursos")]
public class RecursosController : ControllerBase
{
    [HttpGet, RequerPermissao("recurso", "read")] public IActionResult Get() => Ok();
}

public class RecursosTests
{
    private static async Task<WebApplication> CriarApp(Action<WebApplication> mapear, Action<IServiceCollection>? servicos = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(RecursosController).Assembly);
        servicos?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        mapear(app);
        await app.StartAsync();
        return app;
    }

    // ---------- Minimal API + descoberta ----------
    [Fact]
    public async Task Extensao_RequerPermissao_protege_minimal_api()
    {
        await using var app = await CriarApp(a => a.MapGet("/min", () => "ok").RequerPermissao("relatorio", "read"));
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/min")).StatusCode);

        var sem = new HttpRequestMessage(HttpMethod.Get, "/min");
        sem.Headers.Add("X-Perms", "outro:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(sem)).StatusCode);

        var com = new HttpRequestMessage(HttpMethod.Get, "/min");
        com.Headers.Add("X-Perms", "relatorio:read");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(com)).StatusCode);
    }

    [Fact]
    public async Task Descoberta_encontra_endpoints_de_controller_minimal_api_e_policy_escrita_a_mao()
    {
        await using var app = await CriarApp(a =>
        {
            a.MapControllers();
            a.MapGet("/a", () => "a").RequerPermissao("Relatorio", "READ");
            a.MapGet("/b", () => "b").RequireAuthorization("perm:Financeiro:Aprovar");
            a.MapGet("/c", () => "c").RequireAuthorization("perm:invalida"); // formato inválido: ignorada
            a.MapGet("/d", () => "d").RequireAuthorization();                 // sem permissão: nada a descobrir
        });

        var siglas = app.Descobrir();

        Assert.Contains("recurso:read", siglas);
        Assert.Contains("relatorio:read", siglas);
        Assert.Contains("financeiro:aprovar", siglas);
        Assert.DoesNotContain(siglas, s => s.Contains("invalida"));
    }

    [Fact]
    public async Task Descoberta_inclui_catalogo_de_constantes()
    {
        await using var app = await CriarApp(a => a.MapGet("/x", () => "x").AllowAnonymous());
        var siglas = app.Descobrir(Assembly.GetExecutingAssembly());

        Assert.Contains("relatorio:exportar", siglas);   // normalizada
        Assert.Contains("pedido:read", siglas);          // classe aninhada
        Assert.Contains("pedido:cancel", siglas);
    }

    public static class CatalogoInvalido
    {
        public const string Ruim = "semdoispontos";
    }

    [Fact]
    public void Constante_fora_do_formato_falha_rapido()
    {
        // Lê direto a classe inválida marcada no momento do teste
        var tipo = typeof(CatalogoInvalido);
        var metodo = typeof(PermissaoCatalogo).GetMethod("LerConstantes", BindingFlags.NonPublic | BindingFlags.Static)!;
        var constantes = (IEnumerable<string>)metodo.Invoke(null, new object[] { tipo })!;
        Assert.Throws<ArgumentException>(() => constantes.ToList());
    }

    // ---------- Verificação de endpoints ----------
    [Fact]
    public async Task ExigirEndpointsProtegidos_falha_quando_ha_endpoint_sem_declaracao()
    {
        await using var app = await CriarApp(a =>
        {
            a.MapGet("/protegido", () => "p").RequerPermissao("x", "read");
            a.MapGet("/publico", () => "p").AllowAnonymous();
            a.MapGet("/esquecido", () => "p");
        });

        var ex = Assert.Throws<InvalidOperationException>(() => app.ExigirEndpointsProtegidos());
        Assert.Contains("/esquecido", ex.Message);
        Assert.DoesNotContain("/protegido", ex.Message);
        Assert.DoesNotContain("/publico", ex.Message);
    }

    [Fact]
    public async Task ExigirEndpointsProtegidos_passa_quando_tudo_esta_declarado_ou_ignorado()
    {
        await using var app = await CriarApp(a =>
        {
            a.MapControllers();
            a.MapGet("/health", () => "ok");
            a.MapGet("/auth", () => "ok").RequireAuthorization();
        });

        app.ExigirEndpointsProtegidos(e => e.RoutePattern.RawText == "/health");
    }

    // ---------- Auditoria ----------
    [Fact]
    public async Task Auditor_padrao_registra_em_log_com_valores_sanitizados()
    {
        var logs = new List<string>();
        var sp = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ILoggerProvider>(new ListaLoggerProvider(logs, LogLevel.Information))
            .AddAuthorization()
            .AddResourceAuthorization()
            .BuildServiceProvider();

        await sp.GetRequiredService<IPermissaoAuditor>().RegistrarAsync(new PermissaoAuditoriaEvento(
            PermissaoAuditoriaTipo.Concedida, "ana\nFAKE", "usuario 7", "produto:delete", "teste"));

        var log = Assert.Single(logs);
        Assert.Contains("Concedida", log);
        Assert.Contains("produto:delete", log);
        Assert.DoesNotContain('\n', log);
    }

    // ---------- Métricas ----------
    [Fact]
    public async Task Negacoes_geram_metricas()
    {
        var medicoes = new List<(string Nome, long Valor, string Tag)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == AuthorizationMetrics.NomeDoMeter) l.EnableMeasurementEvents(inst);
        };
        // O meter é global: testes em paralelo também publicam medições, então o acesso à lista precisa de lock
        listener.SetMeasurementEventCallback<long>((inst, valor, tags, _) =>
        {
            var item = (inst.Name, valor, string.Join(",", tags.ToArray().Select(t => $"{t.Key}={t.Value}")));
            lock (medicoes) medicoes.Add(item);
        });
        listener.Start();

        await using var app = await CriarApp(a => a.MapGet("/m", () => "m").RequerPermissao("metrica", "read"));
        var client = app.GetTestClient();

        await client.GetAsync("/m"); // 401
        var req = new HttpRequestMessage(HttpMethod.Get, "/m");
        req.Headers.Add("X-Perms", "outra:coisa");
        await client.SendAsync(req); // 403

        lock (medicoes)
        {
            Assert.Contains(medicoes, m => m.Nome == "prime.authorization.requisicao_negada" && m.Tag == "status_code=401");
            Assert.Contains(medicoes, m => m.Nome == "prime.authorization.requisicao_negada" && m.Tag == "status_code=403");
            Assert.Contains(medicoes, m => m.Nome == "prime.authorization.permissao_negada" && m.Tag == "permissao=metrica:read");
        }
    }
}

public sealed class ListaLoggerProvider(List<string> logs, LogLevel minimo) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new L(logs, minimo);
    public void Dispose() { }

    private sealed class L(List<string> logs, LogLevel minimo) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimo;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= minimo) lock (logs) logs.Add(formatter(state, exception));
        }
    }
}
