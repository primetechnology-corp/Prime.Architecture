using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.EntityFramework.Entities;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Extensions;
using Prime.Architecture.Authorization.Handlers;
using Prime.Architecture.Authorization.Policies;
using Xunit;

namespace Prime.Architecture.Authorization.SecurityTests;

/// <summary>
/// Fuzzing determinístico (semente fixa): entradas aleatórias e hostis contra invariantes que nunca podem quebrar.
/// Para repetir com outra semente: variável de ambiente FUZZ_SEED.
/// </summary>
public static class Fuzz
{
    public static int Seed => int.TryParse(Environment.GetEnvironmentVariable("FUZZ_SEED"), out var s) ? s : 20261009;

    private static readonly string[] Pedacos =
    {
        "produto", "Produto", "PRODUTO", "read", "Read", "create", "delete", "admin", "manage", ":", "::", ":::", " ", "  ", "\t", "\n", "\r\n",
        "\0", "*", "%", "'", "\"", ";", "--", "/", "\\", "..", "{", "}", "perm:", "İ", "ı", "ß", "ǅ", "ﬃ", "é", "é", "😀", "‮", "​",
        "：", "<script>", "' OR 1=1 --", "${jndi:ldap://x}", "%00", "%0a", "%2e%2e"
    };

    public static string Texto(Random r, int maxPedacos = 6)
    {
        var sb = new StringBuilder();
        var n = r.Next(0, maxPedacos + 1);
        for (var i = 0; i < n; i++)
        {
            switch (r.Next(4))
            {
                case 0: sb.Append(Pedacos[r.Next(Pedacos.Length)]); break;
                case 1: sb.Append((char)r.Next(0x20, 0x7f)); break;
                case 2: sb.Append((char)r.Next(0, 0x20)); break;           // controle
                default: sb.Append(char.ConvertFromUtf32(r.Next(0x80, 0x2FFF))); break;
            }
        }
        return sb.ToString();
    }
}

public class FuzzTests
{
    private static IAuthorizationService CriarServico()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddResourceAuthorization();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public async Task Oraculo_so_autoriza_quando_alguma_claim_e_exatamente_a_permissao()
    {
        var auth = CriarServico();
        var r = new Random(Fuzz.Seed);
        var vocab = new[] { "produto", "pedido", "admin", "relatorio" };
        var acoes = new[] { "read", "create", "delete", "manage" };

        for (var i = 0; i < 20_000; i++)
        {
            var recurso = vocab[r.Next(vocab.Length)];
            var acao = acoes[r.Next(acoes.Length)];
            var exigida = $"{recurso}:{acao}";

            var valores = new List<string>();
            for (var k = r.Next(0, 6); k > 0; k--)
            {
                valores.Add(r.Next(10) switch
                {
                    0 => exigida,
                    1 => exigida.ToUpperInvariant(),
                    2 => exigida + Fuzz.Texto(r, 2),     // sufixo hostil
                    3 => Fuzz.Texto(r, 2) + exigida,     // prefixo hostil
                    4 => exigida.Replace(":", "："), // dois-pontos de largura total
                    5 => recurso + ":*",
                    _ => Fuzz.Texto(r)
                });
            }

            var user = new ClaimsPrincipal(new ClaimsIdentity(valores.Select(v => new Claim("permissao", v)), "teste"));
            var esperado = valores.Any(v => string.Equals(v, exigida, StringComparison.OrdinalIgnoreCase));

            var resultado = await auth.AuthorizeAsync(user, $"perm:{exigida}");
            Assert.True(esperado == resultado.Succeeded,
                $"seed={Fuzz.Seed} iter={i} exigida='{exigida}' claims=[{string.Join(" | ", valores.Select(Escape))}] esperado={esperado} obtido={resultado.Succeeded}");
        }
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c < 0x20 || c > 0x7e ? $"\\u{(int)c:X4}" : c.ToString()));

    [Fact]
    public void Sigla_ou_lanca_ArgumentException_ou_devolve_formato_valido()
    {
        var r = new Random(Fuzz.Seed + 1);
        for (var i = 0; i < 50_000; i++)
        {
            var recurso = Fuzz.Texto(r);
            var acao = Fuzz.Texto(r);

            string sigla;
            try { sigla = RequerPermissaoAttribute.Sigla(recurso, acao); }
            catch (ArgumentException) { continue; }

            var partes = sigla.Split(':');
            Assert.True(partes.Length == 2 && partes[0].Length > 0 && partes[1].Length > 0 &&
                        !string.IsNullOrWhiteSpace(partes[0]) && !string.IsNullOrWhiteSpace(partes[1]) &&
                        sigla == sigla.ToLowerInvariant(),
                $"seed={Fuzz.Seed + 1} iter={i} sigla inválida gerada: '{Escape(sigla)}'");
        }
    }

    [Fact]
    public void Atributo_nunca_lanca_excecao_diferente_de_ArgumentException()
    {
        var r = new Random(Fuzz.Seed + 2);
        for (var i = 0; i < 50_000; i++)
        {
            try { _ = new RequerPermissaoAttribute(Fuzz.Texto(r), Fuzz.Texto(r)); }
            catch (ArgumentException) { }
        }
    }

    [Fact]
    public void LogSanitizer_nunca_deixa_caractere_de_controle_nem_estoura_o_limite()
    {
        var r = new Random(Fuzz.Seed + 3);
        for (var i = 0; i < 50_000; i++)
        {
            var entrada = r.Next(50) == 0 ? new string('x', r.Next(100, 5000)) + Fuzz.Texto(r) : Fuzz.Texto(r, 20);
            var saida = LogSanitizer.Limpar(entrada);

            Assert.True(saida.Length <= 101, $"seed={Fuzz.Seed + 3} iter={i} tamanho={saida.Length}");
            Assert.DoesNotContain(saida, c => char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029);
        }
    }

    [Fact]
    public async Task PolicyProvider_nunca_lanca_e_so_cria_policy_com_o_prefixo()
    {
        var sp = new ServiceCollection().AddLogging().AddAuthorization().AddResourceAuthorization().BuildServiceProvider();
        var provider = sp.GetRequiredService<IAuthorizationPolicyProvider>();
        var r = new Random(Fuzz.Seed + 4);

        for (var i = 0; i < 20_000; i++)
        {
            var nome = r.Next(3) == 0 ? "perm:" + Fuzz.Texto(r) : Fuzz.Texto(r);
            var policy = await provider.GetPolicyAsync(nome);   // não pode lançar

            if (!nome.StartsWith("perm:", StringComparison.Ordinal))
                Assert.Null(policy);
            else
                Assert.Contains(policy!.Requirements, req => req is Requirements.PermissaoRequirement);
        }
    }

    [Fact]
    public async Task Sincronizacao_com_siglas_aleatorias_nunca_viola_unicidade_nem_lanca_excecao_inesperada()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var db = new AttackDbContext(new DbContextOptionsBuilder<AttackDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        var r = new Random(Fuzz.Seed + 5);
        for (var rodada = 0; rodada < 200; rodada++)
        {
            var siglas = Enumerable.Range(0, r.Next(0, 30)).Select(_ => Fuzz.Texto(r, 3)).ToList();
            siglas.AddRange(siglas.Take(3).Select(s => s.ToUpperInvariant()));   // duplicadas por caixa

            try
            {
                await db.SincronizarAsync(siglas, opcoes: new SincronizacaoOpcoes { LimiteDesativacao = 1 });
            }
            catch (InvalidOperationException) { /* trava de segurança: aceitável */ }

            var todas = db.Set<Permissao>().Select(p => p.Sigla).ToList();
            Assert.Equal(todas.Count, todas.Select(s => s.ToLowerInvariant()).Distinct().Count());
        }
    }

    [Fact]
    public async Task Resolver_de_claims_nunca_lanca_com_claims_hostis()
    {
        var sp = new ServiceCollection().AddLogging().AddAuthorization().AddResourceAuthorization().BuildServiceProvider();
        var resolver = sp.GetRequiredService<Interfaces.IPermissaoResolver>();
        var r = new Random(Fuzz.Seed + 6);

        for (var i = 0; i < 20_000; i++)
        {
            var claims = Enumerable.Range(0, r.Next(0, 8)).Select(_ => new Claim(r.Next(2) == 0 ? "permissao" : Fuzz.Texto(r, 2).DefaultIfEmpty('x').Aggregate("", (a, c) => a + c), Fuzz.Texto(r)));
            var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "teste"));
            var set = await resolver.ObterAsync(user);
            Assert.NotNull(set);
        }
    }
}

/// <summary>Fuzzing de HTTP contra um servidor com JwtBearer estrito (reusa a API dos ataques).</summary>
public class HttpFuzzTests : IAsyncDisposable
{
    private readonly AttackTests _alvo = new();

    public ValueTask DisposeAsync() => _alvo.DisposeAsync();

    private static readonly string[] Segmentos = { "api", "produtos", "API", "Produtos", "1", "sem-atributo", "publico", ".", "..", "%2e", "%2f", "%00", "%0a", ";", "", "é", "😀" };
    private static readonly string[] Verbos = { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE", "FOO" };

    [Fact]
    public async Task Caminhos_e_cabecalhos_aleatorios_nunca_dao_200_sem_token_valido_nem_5xx()
    {
        var r = new Random(Fuzz.Seed + 7);
        var cliente = _alvo.ClienteParaFuzz;
        var ok = 0;

        for (var i = 0; i < 4_000; i++)
        {
            var caminho = "/" + string.Join("/", Enumerable.Range(0, r.Next(1, 5)).Select(_ => Segmentos[r.Next(Segmentos.Length)]));
            if (r.Next(4) == 0) caminho += "?" + Fuzz.Texto(r, 2).Replace(" ", "%20").Replace("\0", "").Replace("\n", "").Replace("\r", "");

            var req = new HttpRequestMessage();
            try { req.Method = new HttpMethod(Verbos[r.Next(Verbos.Length)]); req.RequestUri = new Uri(caminho, UriKind.Relative); }
            catch (Exception) { continue; }   // URI/método inválidos nem chegam a ser enviados

            // 50% sem Authorization, 50% com lixo
            if (r.Next(2) == 0)
                req.Headers.TryAddWithoutValidation("Authorization", r.Next(3) switch
                {
                    0 => "Bearer " + Fuzz.Texto(r, 5).Replace("\n", "").Replace("\r", "").Replace("\0", ""),
                    1 => "Bearer " + new string('A', r.Next(100, 9000)),
                    _ => Fuzz.Texto(r, 3).Replace("\n", "").Replace("\r", "").Replace("\0", "")
                });
            if (r.Next(5) == 0) req.Headers.TryAddWithoutValidation("X-Perms", "produto:read");   // cabeçalho que não existe nesta API

            HttpResponseMessage resposta;
            try { resposta = await cliente.SendAsync(req); }
            catch (Exception e) when (e is InvalidOperationException or UriFormatException) { continue; }   // o cliente/host rejeita caminhos inválidos antes do pipeline

            var corpo = await resposta.Content.ReadAsStringAsync();
            Assert.True((int)resposta.StatusCode < 500, $"seed={Fuzz.Seed + 7} iter={i} {req.Method} {caminho} => {(int)resposta.StatusCode}");

            // Nada de dados protegidos sem token válido; só /publico pode responder 200 com conteúdo
            if (resposta.StatusCode == HttpStatusCode.OK && !caminho.ToLowerInvariant().Contains("publico"))
                Assert.True(string.IsNullOrEmpty(corpo) || !(corpo.Contains("lista") || corpo.Contains("criado") || corpo.Contains("excluido") || corpo.Contains("esquecido")),
                    $"seed={Fuzz.Seed + 7} iter={i} {req.Method} {caminho} vazou '{corpo}' sem token válido");

            ok++;
        }

        Assert.True(ok > 3_000, $"poucas requisições efetivas: {ok}");
    }
}
