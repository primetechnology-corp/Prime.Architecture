using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Auditing;
using Prime.Architecture.Authorization.Configuration;
using Prime.Architecture.Authorization.Handlers;
using Prime.Architecture.Authorization.Implementation;
using Prime.Architecture.Authorization.Interfaces;
using Prime.Architecture.Authorization.Policies;

namespace Prime.Architecture.Authorization.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra o ResourceAuthorize. Chame junto de <c>AddAuthorization()</c>.
    /// </summary>
    public static IServiceCollection AddResourceAuthorization(
        this IServiceCollection services,
        Action<ResourceAuthorizationOptions>? configure = null)
    {
        services.AddOptions<ResourceAuthorizationOptions>();
        if (configure != null)
            services.Configure(configure);

        services.TryAddScoped<IPermissaoResolver, ClaimsPermissaoResolver>();
        services.TryAddSingleton<IPermissaoAuditor, LogPermissaoAuditor>();
        services.AddScoped<IAuthorizationHandler, PermissaoHandler>();
        services.Replace(ServiceDescriptor.Singleton<IAuthorizationPolicyProvider, PermissaoPolicyProvider>());

        services.Replace(ServiceDescriptor.Singleton<IAuthorizationMiddlewareResultHandler, RespostaAutorizacaoHandler>());

        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<ResourceAuthorizationOptions>>((authorization, resource) =>
            {
                if (resource.Value.ExigirAutenticacaoPorPadrao)
                    authorization.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            });

        return services;
    }
}
