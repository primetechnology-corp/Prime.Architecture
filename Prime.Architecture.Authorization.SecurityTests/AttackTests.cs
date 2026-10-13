using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.Extensions;
using Xunit;

namespace Prime.Architecture.Authorization.SecurityTests;

[ApiController, Route("api/produtos")]
public class ProdutosController : ControllerBase
{
    [HttpGet, RequerPermissao("produto", "read")] public IActionResult Get() => Ok("lista");
    [HttpPost, RequerPermissao("produto", "create")] public IActionResult Post() => Ok("criado");
    [HttpDelete("{id}"), RequerPermissao("produto", "delete")] public IActionResult Delete(int id) => Ok("excluido");
    [HttpGet("sem-atributo")] public IActionResult SemAtributo() => Ok("esquecido");
    [HttpGet("publico"), AllowAnonymous] public IActionResult Publico() => Ok("publico");
}

public sealed class ListaLoggerProvider(List<string> logs) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new L(logs);
    public void Dispose() { }

    private sealed class L(List<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) lock (logs) logs.Add(formatter(state, exception));
        }
    }
}

/// <summary>Ataques contra uma API configurada como em produção: JwtBearer estrito + ResourceAuthorize.</summary>
public class AttackTests : IAsyncDisposable
{
    private const string Chave = "ChaveSecretaDeTesteComMaisDe32Caracteres!!";
    private const string Issuer = "prime-test";
    private const string Audience = "prime-clients";

    private readonly WebApplication _app;
    private readonly HttpClient _client;
    protected readonly List<string> Logs = new();
    public HttpClient ClienteParaFuzz => _client;

    public AttackTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<ILoggerProvider>(new ListaLoggerProvider(Logs));
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = Audience,
                ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Chave))
            });
        builder.Services.AddAuthorization();
        builder.Services.AddResourceAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(ProdutosController).Assembly);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        _app.StartAsync().GetAwaiter().GetResult();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ---------- helpers ----------
    private static string Token(IEnumerable<Claim> claims, string chave = Chave, string issuer = Issuer, string audience = Audience,
        DateTime? expires = null, DateTime? notBefore = null)
    {
        var jwt = new JwtSecurityToken(issuer, audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1), expires ?? DateTime.UtcNow.AddMinutes(30),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static Claim[] Perms(params string[] p) => p.Select(x => new Claim("permissao", x)).ToArray();

    private static string B64(string s) => Base64UrlEncoder.Encode(s);

    private async Task<HttpResponseMessage> Enviar(HttpMethod m, string url, string? token = null, Action<HttpRequestMessage>? ajustar = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (token != null) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        ajustar?.Invoke(req);
        return await _client.SendAsync(req);
    }

    private static void NaoAutorizado(HttpResponseMessage r)
        => Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
                           or HttpStatusCode.MethodNotAllowed or HttpStatusCode.BadRequest,
            $"Esperado bloqueio, obtido {(int)r.StatusCode}");

    // ---------- 1. Ataques ao token ----------
    [Fact]
    public async Task Token_alg_none_sem_assinatura_e_recusado()
    {
        var header = B64("{\"alg\":\"none\",\"typ\":\"JWT\"}");
        var payload = B64($"{{\"permissao\":[\"produto:read\"],\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos", $"{header}.{payload}.")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos", $"{header}.{payload}")).StatusCode);
    }

    [Fact]
    public async Task Payload_adulterado_com_assinatura_antiga_e_recusado()
    {
        var valido = Token(Perms("produto:read"));
        var partes = valido.Split('.');
        var forjado = B64($"{{\"permissao\":[\"produto:read\",\"produto:delete\"],\"iss\":\"{Issuer}\",\"aud\":\"{Audience}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}");
        var r = await Enviar(HttpMethod.Delete, "/api/produtos/1", $"{partes[0]}.{forjado}.{partes[2]}");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Token_assinado_com_outra_chave_e_recusado()
        => Assert.Equal(HttpStatusCode.Unauthorized,
            (await Enviar(HttpMethod.Get, "/api/produtos", Token(Perms("produto:read"), chave: "OutraChaveDiferenteComMaisDe32Caracteres!!"))).StatusCode);

    [Fact]
    public async Task Token_expirado_e_recusado()
        => Assert.Equal(HttpStatusCode.Unauthorized,
            (await Enviar(HttpMethod.Get, "/api/produtos",
                Token(Perms("produto:read"), notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddSeconds(-5)))).StatusCode);

    [Fact]
    public async Task Token_com_issuer_ou_audience_errados_e_recusado()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos", Token(Perms("produto:read"), issuer: "atacante"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos", Token(Perms("produto:read"), audience: "outro-sistema"))).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("a.b.c")]
    [InlineData("Bearer")]
    public async Task Tokens_malformados_sao_recusados(string token)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos", token)).StatusCode);

    // ---------- 2. Contorno de rota / verbo ----------
    [Theory]
    [InlineData("/api/produtos")]
    [InlineData("/API/PRODUTOS")]
    [InlineData("/api/produtos/")]
    [InlineData("//api/produtos")]
    [InlineData("/api/./produtos")]
    [InlineData("/api/produtos/../produtos")]
    [InlineData("/api/%70rodutos")]
    [InlineData("/api/produtos;x=1")]
    [InlineData("/api/produtos%00")]
    [InlineData("/api/produtos%2f")]
    [InlineData("/api/produtos?x=1")]
    public async Task Variacoes_de_rota_nao_contornam_a_autorizacao_sem_token(string url)
    {
        try
        {
            var r = await Enviar(HttpMethod.Get, url);
            Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        }
        catch (InvalidOperationException)
        {
            // O host rejeita caminhos inválidos (ex.: byte nulo) antes do pipeline: também é bloqueio
        }
    }

    [Theory]
    [InlineData("/API/PRODUTOS")]
    [InlineData("/api/produtos/")]
    [InlineData("/api/%70rodutos")]
    public async Task Variacoes_de_rota_continuam_exigindo_a_permissao(string url)
    {
        var r = await Enviar(HttpMethod.Get, url, Token(Perms("outro:read")));
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    [InlineData("PATCH")]
    [InlineData("PUT")]
    public async Task Verbos_incomuns_nao_dao_acesso_sem_permissao(string verbo)
    {
        var r = await Enviar(new HttpMethod(verbo), "/api/produtos", Token(Perms("outro:read")));
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Theory]
    [InlineData("X-HTTP-Method-Override")]
    [InlineData("X-HTTP-Method")]
    [InlineData("X-Method-Override")]
    public async Task Override_de_metodo_nao_escala_privilegio(string header)
    {
        // Usuário só com read tenta apagar usando override
        var token = Token(Perms("produto:read"));
        var r = await Enviar(HttpMethod.Post, "/api/produtos/1", token, req => req.Headers.Add(header, "DELETE"));
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
        var r2 = await Enviar(HttpMethod.Post, "/api/produtos", token, req => req.Headers.Add(header, "GET"));
        Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
    }

    [Fact]
    public async Task Endpoint_esquecido_sem_atributo_nao_e_publico()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, "/api/produtos/sem-atributo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/produtos/publico")).StatusCode);
    }

    // ---------- 3. Manipulação de valores de claim ----------
    [Theory]
    [InlineData("produto:read ")]        // espaço no fim
    [InlineData(" produto:read")]        // espaço no início
    [InlineData("produto:read\0")]       // byte nulo
    [InlineData("produto：read")]        // dois-pontos de largura total
    [InlineData("produto:read\n")]
    [InlineData("İtem:read")]
    [InlineData("produto:*")]            // curinga não existe
    [InlineData("*")]
    [InlineData("*:*")]
    [InlineData("produto:")]
    [InlineData("produto")]
    [InlineData("")]
    [InlineData("produto:read,produto:create")] // lista dentro de um valor só
    [InlineData("produto:read;produto:create")]
    public async Task Claims_parecidas_ou_curingas_nao_concedem_create(string valor)
    {
        var r = await Enviar(HttpMethod.Post, "/api/produtos", Token(Perms(valor)));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Theory]
    [InlineData("role", "produto:create")]
    [InlineData("scope", "produto:create")]
    [InlineData("permissions", "produto:create")]
    [InlineData("perm", "produto:create")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "produto:create")]
    public async Task Outros_tipos_de_claim_nao_concedem_permissao(string tipo, string valor)
    {
        var r = await Enviar(HttpMethod.Post, "/api/produtos", Token(new[] { new Claim(tipo, valor) }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Permissao_de_outra_acao_ou_recurso_nao_vale()
    {
        var token = Token(Perms("produto:read", "pedido:create", "produto:update"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Post, "/api/produtos", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Delete, "/api/produtos/1", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, "/api/produtos", token)).StatusCode);
    }

    [Fact]
    public async Task Token_sem_nenhuma_permissao_e_negado_e_auditado()
    {
        var r = await Enviar(HttpMethod.Get, "/api/produtos", Token(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains(Logs, l => l.Contains("Acesso negado") && l.Contains("7"));
    }

    // ---------- 4. Log forging (injeção em logs) ----------
    [Fact]
    public async Task Id_de_usuario_com_quebra_de_linha_nao_forja_linhas_de_log()
    {
        var token = Token(new[] { new Claim(ClaimTypes.NameIdentifier, "7\nWarning: FAKE admin logou com sucesso") });
        await Enviar(HttpMethod.Get, "/api/produtos", token);

        Assert.All(Logs, l => Assert.DoesNotContain('\n', l));
        Assert.All(Logs, l => Assert.DoesNotContain('\r', l));
    }

    [Fact]
    public async Task Caminho_com_quebra_de_linha_nao_forja_linhas_de_log()
    {
        await Enviar(HttpMethod.Get, "/api/produtos%0aWarning:%20FAKE%0d%0aentrada-falsa");

        Assert.All(Logs, l => Assert.DoesNotContain('\n', l));
        Assert.All(Logs, l => Assert.DoesNotContain('\r', l));
    }

    // ---------- 5. Corpo/cabeçalhos hostis ----------
    [Fact]
    public async Task Cabecalhos_de_identidade_forjados_sao_ignorados()
    {
        var r = await Enviar(HttpMethod.Get, "/api/produtos", null, req =>
        {
            req.Headers.Add("X-Perms", "produto:read");
            req.Headers.Add("X-User-Id", "1");
            req.Headers.Add("X-Forwarded-User", "admin");
            req.Headers.Add("X-Permissao", "produto:read");
        });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Varias_requisicoes_paralelas_sem_permissao_nunca_retornam_200()
    {
        var token = Token(Perms("produto:read"));
        var tarefas = Enumerable.Range(0, 200).Select(_ => Enviar(HttpMethod.Post, "/api/produtos", token));
        var respostas = await Task.WhenAll(tarefas);
        Assert.All(respostas, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }
}
