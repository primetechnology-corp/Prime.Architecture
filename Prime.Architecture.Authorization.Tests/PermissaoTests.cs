using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.Discovery;
using Prime.Architecture.Authorization.Extensions;
using Xunit;

namespace Prime.Architecture.Authorization.Tests;

public class PermissaoTests
{
    private static IAuthorizationService CriarServico()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddResourceAuthorization();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Usuario(params string[] permissoes)
    {
        var claims = permissoes.Select(p => new Claim("permissao", p));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "teste"));
    }

    [Fact]
    public async Task Autoriza_quando_possui_a_permissao()
    {
        var result = await CriarServico().AuthorizeAsync(Usuario("perfil:read"), "perm:perfil:read");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Ignora_maiusculas_na_policy_e_na_claim()
    {
        var result = await CriarServico().AuthorizeAsync(Usuario("Perfil:READ"), "perm:Perfil:Read");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Nega_quando_nao_possui_a_permissao()
    {
        var result = await CriarServico().AuthorizeAsync(Usuario("perfil:read"), "perm:perfil:delete");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Nega_usuario_nao_autenticado()
    {
        var anonimo = new ClaimsPrincipal(new ClaimsIdentity());
        var result = await CriarServico().AuthorizeAsync(anonimo, "perm:perfil:read");
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Atributo_gera_policy_normalizada()
    {
        Assert.Equal("perm:tipo-conta:read", new RequerPermissaoAttribute("Tipo-Conta", "READ").Policy);
    }

    [Fact]
    public void Catalogo_descobre_permissoes_de_classes_e_metodos()
    {
        var siglas = PermissaoCatalogo.Descobrir(Assembly.GetExecutingAssembly());
        Assert.Contains("exemplo:read", siglas);
        Assert.Contains("exemplo:reactivate", siglas);
    }

    [Theory]
    [InlineData("", "read")]
    [InlineData("  ", "read")]
    [InlineData("a", "")]
    [InlineData("a:b", "c")]
    [InlineData("a", "b:c")]
    public void Atributo_rejeita_recurso_ou_acao_invalidos(string recurso, string acao)
        => Assert.Throws<ArgumentException>(() => new RequerPermissaoAttribute(recurso, acao));

    private class ResolverQueFalha : Prime.Architecture.Authorization.Interfaces.IPermissaoResolver
    {
        public Task<IReadOnlySet<string>> ObterAsync(ClaimsPrincipal user, CancellationToken ct = default)
            => throw new InvalidOperationException("falha ao consultar permissões");
    }

    [Fact]
    public async Task Falha_do_resolver_nunca_autoriza_e_e_registrada()
    {
        var logs = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new ListaLoggerProvider(logs));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        services.AddScoped<Prime.Architecture.Authorization.Interfaces.IPermissaoResolver, ResolverQueFalha>();
        var auth = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var resultado = await auth.AuthorizeAsync(Usuario("x:y"), "perm:x:y");

        Assert.False(resultado.Succeeded);
        Assert.Contains(logs, l => l.Contains("Falha ao obter permissões"));
    }

    [Fact]
    public async Task Negacao_e_registrada_no_log_sem_vazar_claims()
    {
        var logs = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new ListaLoggerProvider(logs));
        services.AddAuthorization();
        services.AddResourceAuthorization();
        var auth = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "42"),
            new Claim("permissao", "segredo:read")
        }, "teste"));

        await auth.AuthorizeAsync(user, "perm:perfil:delete");

        var log = Assert.Single(logs);
        Assert.Contains("42", log);
        Assert.Contains("perfil:delete", log);
        Assert.DoesNotContain("segredo:read", log);
    }

    private sealed class ListaLoggerProvider(List<string> logs) : Microsoft.Extensions.Logging.ILoggerProvider
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new ListaLogger(logs);
        public void Dispose() { }

        private sealed class ListaLogger(List<string> logs) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
                    logs.Add(formatter(state, exception));
            }
        }
    }

    [RequerPermissao("Exemplo", "read")]
    private class ExemploController
    {
        [RequerPermissao("exemplo", "reactivate")]
        public void Reativar() { }
    }
}
