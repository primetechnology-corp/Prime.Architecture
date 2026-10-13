using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.Extensions;
using Xunit;

namespace Prime.Architecture.Authorization.Tests;

/// <summary>
/// Autenticação de teste: cabeçalho "X-Perms" com permissões separadas por vírgula.
/// Sem o cabeçalho, o usuário é anônimo (não autenticado).
/// </summary>
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Perms", out var header))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = header.ToString()
            .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
            .Select(p => new Claim("permissao", p.Trim()));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

[ApiController]
[Route("api/itens")]
public class ItensController : ControllerBase
{
    [HttpGet, RequerPermissao("item", "read")]
    public IActionResult Listar() => Ok("lista");

    [HttpPost, RequerPermissao("item", "create")]
    public IActionResult Criar() => Ok("criado");

    [HttpPost("{id}/reativar"), RequerPermissao("item", "reactivate")]
    public IActionResult Reativar(int id) => Ok("reativado");

    [HttpGet("publico"), AllowAnonymous]
    public IActionResult Publico() => Ok("publico");
}

[ApiController]
[Route("api/protegido")]
[RequerPermissao("protegido", "read")]
public class ProtegidoController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("classe");

    // Dois atributos: exige as duas permissões (AND)
    [HttpGet("duplo"), RequerPermissao("protegido", "update")]
    public IActionResult Duplo() => Ok("duplo");
}

public abstract class HttpPipelineBase : System.IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    protected HttpPipelineBase(System.Action<Prime.Architecture.Authorization.Configuration.ResourceAuthorizationOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization(configure);
        builder.Services.AddControllers().AddApplicationPart(typeof(ItensController).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.MapGet("/min/itens", () => "min").RequireAuthorization("perm:item:read");
        _app.MapGet("/min/aberto", () => "aberto");
        _app.StartAsync().GetAwaiter().GetResult();

        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    protected Task<HttpResponseMessage> Enviar(HttpMethod metodo, string url, string? permissoes = null)
    {
        var req = new HttpRequestMessage(metodo, url);
        if (permissoes != null)
            req.Headers.Add("X-Perms", permissoes);
        return _client.SendAsync(req);
    }
}

public class HttpPipelineTests : HttpPipelineBase
{
    public HttpPipelineTests() : base(null) { }

    [Fact]
    public async Task Sem_token_retorna_401()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/itens")).StatusCode);

    [Fact]
    public async Task Autenticado_sem_permissao_retorna_403()
        => Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Get, "/api/itens", "outro:read")).StatusCode);

    [Fact]
    public async Task Com_permissao_retorna_200()
        => Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/itens", "item:read")).StatusCode);

    [Fact]
    public async Task Permissao_de_outra_acao_nao_vale()
        => Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Post, "/api/itens", "item:read")).StatusCode);

    [Fact]
    public async Task Acao_customizada_exige_a_permissao_customizada()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Post, "/api/itens/1/reativar", "item:create")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Post, "/api/itens/1/reativar", "item:reactivate")).StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_libera_sem_token()
        => Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/itens/publico")).StatusCode);

    [Fact]
    public async Task Atributo_na_classe_protege_todas_as_actions()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/protegido")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/protegido", "protegido:read")).StatusCode);
    }

    [Fact]
    public async Task Atributos_na_classe_e_no_metodo_exigem_ambas_as_permissoes()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Get, "/api/protegido/duplo", "protegido:read")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Get, "/api/protegido/duplo", "protegido:update")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/protegido/duplo", "protegido:read,protegido:update")).StatusCode);
    }

    [Fact]
    public async Task Minimal_api_respeita_a_policy_dinamica()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/min/itens")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Get, "/min/itens", "x:y")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/min/itens", "item:read")).StatusCode);
    }

    [Fact]
    public async Task Endpoint_sem_atributo_exige_autenticacao_por_padrao()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/min/aberto")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/min/aberto", "qualquer:coisa")).StatusCode);
    }
}

public class RespostaPadronizadaTests : HttpPipelineBase
{
    public RespostaPadronizadaTests() : base(o =>
    {
        o.ExigirAutenticacaoPorPadrao = true;
        o.EscreverResposta = (ctx, status) => ctx.Response.WriteAsync($"{{\"status\":{status}}}");
    })
    {
    }

    [Fact]
    public async Task Respostas_401_e_403_usam_o_escritor_customizado()
    {
        var r401 = await Enviar(HttpMethod.Get, "/api/itens");
        Assert.Equal(HttpStatusCode.Unauthorized, r401.StatusCode);
        Assert.Equal("{\"status\":401}", await r401.Content.ReadAsStringAsync());

        var r403 = await Enviar(HttpMethod.Get, "/api/itens", "x:y");
        Assert.Equal(HttpStatusCode.Forbidden, r403.StatusCode);
        Assert.Equal("{\"status\":403}", await r403.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Endpoint_sem_atributo_exige_autenticacao_quando_habilitado()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/min/aberto")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/min/aberto", "qualquer:coisa")).StatusCode);
    }

    [Fact]
    public async Task AllowAnonymous_continua_publico_com_fallback()
        => Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/itens/publico")).StatusCode);
}

public class FallbackDesligadoTests : HttpPipelineBase
{
    public FallbackDesligadoTests() : base(o => o.ExigirAutenticacaoPorPadrao = false) { }

    [Fact]
    public async Task Endpoint_sem_atributo_fica_aberto_quando_o_padrao_seguro_e_desligado()
        => Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/min/aberto")).StatusCode);
}

public class HttpFalhaDoResolverTests
{
    private sealed class ResolverQueFalha : Prime.Architecture.Authorization.Interfaces.IPermissaoResolver
    {
        public System.Threading.Tasks.Task<System.Collections.Generic.IReadOnlySet<string>> ObterAsync(
            ClaimsPrincipal user, System.Threading.CancellationToken ct = default)
            => throw new System.InvalidOperationException("banco fora do ar");
    }

    [Fact]
    public async Task Banco_fora_do_ar_retorna_403_e_nao_500()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization();
        builder.Services.AddScoped<Prime.Architecture.Authorization.Interfaces.IPermissaoResolver, ResolverQueFalha>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ItensController).Assembly);

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        using var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/itens");
        req.Headers.Add("X-Perms", "item:read");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(req)).StatusCode);

        // Sem autenticação continua 401 (o resolver nem é consultado)
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/itens")).StatusCode);

        // Rota pública não depende do resolver
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/itens/publico")).StatusCode);
    }
}
