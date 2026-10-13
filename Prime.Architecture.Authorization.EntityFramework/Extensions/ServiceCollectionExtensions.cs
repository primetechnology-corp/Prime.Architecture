using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prime.Architecture.Authorization.EntityFramework.Configuration;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using Prime.Architecture.Authorization.Interfaces;

namespace Prime.Architecture.Authorization.EntityFramework.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Troca o resolver padrão (claims) pelo resolver que consulta o banco, com cache.
    /// O perfil do usuário é lido do banco pelo <typeparamref name="TUsuarioPerfilProvider"/> (recomendado):
    /// mudanças de perfil e bloqueios valem sem esperar o token expirar.
    /// Chame junto de <c>AddResourceAuthorization()</c>, em qualquer ordem.
    /// </summary>
    public static IServiceCollection AddResourceAuthorizationEntityFramework<TContext, TUsuarioPerfilProvider>(
        this IServiceCollection services,
        Action<EfPermissaoOptions>? configure = null)
        where TContext : DbContext
        where TUsuarioPerfilProvider : class, IUsuarioPerfilProvider
    {
        services.AddEfCore<TContext>(configure);

        // O provider da aplicação fica dentro de um decorator com cache (e proteção contra stampede)
        services.AddScoped<TUsuarioPerfilProvider>();
        services.Replace(ServiceDescriptor.Scoped<IUsuarioPerfilProvider>(sp => new CachedUsuarioPerfilProvider(
            sp.GetRequiredService<TUsuarioPerfilProvider>(),
            sp.GetRequiredService<SingleFlightCache>(),
            sp.GetRequiredService<IOptions<EfPermissaoOptions>>())));
        return services;
    }

    /// <summary>
    /// Variante que lê o perfil do token. Exige <c>o.ConfiarNoPerfilDoToken = true</c> (opt-in explícito),
    /// pois mudanças de perfil e bloqueios só valem quando o token expira.
    /// </summary>
    public static IServiceCollection AddResourceAuthorizationEntityFramework<TContext>(
        this IServiceCollection services,
        Action<EfPermissaoOptions>? configure = null) where TContext : DbContext
    {
        var teste = new EfPermissaoOptions();
        configure?.Invoke(teste);

        if (!teste.ConfiarNoPerfilDoToken)
            throw new InvalidOperationException(
                "Informe como obter o perfil vigente do usuário: use AddResourceAuthorizationEntityFramework<TContext, TUsuarioPerfilProvider>() " +
                "(recomendado, lê do banco) ou defina ConfiarNoPerfilDoToken = true para aceitar o perfil do token.");

        services.AddEfCore<TContext>(configure);
        services.Replace(ServiceDescriptor.Scoped<IUsuarioPerfilProvider, ClaimsUsuarioPerfilProvider>());
        return services;
    }

    private static void AddEfCore<TContext>(this IServiceCollection services, Action<EfPermissaoOptions>? configure)
        where TContext : DbContext
    {
        services.AddMemoryCache();
        services.AddOptions<EfPermissaoOptions>();
        if (configure != null)
            services.Configure(configure);

        services.Replace(ServiceDescriptor.Scoped<IPermissaoResolver, EfPermissaoResolver<TContext>>());
        services.AddSingleton<SingleFlightCache>();
        services.AddSingleton<InstanciaId>();
        services.AddSingleton<IPermissaoCacheInvalidator, PermissaoCacheInvalidator>();
    }

    /// <summary>
    /// Registra o canal de invalidação de cache entre instâncias (ex.: Redis pub/sub). Com ele,
    /// <c>InvalidarPerfilAsync</c>/<c>InvalidarUsuarioAsync</c> limpam o cache de todas as instâncias.
    /// </summary>
    public static IServiceCollection AddPermissaoInvalidationBus<TBus>(this IServiceCollection services)
        where TBus : class, IPermissaoInvalidationBus
    {
        services.Replace(ServiceDescriptor.Singleton<IPermissaoInvalidationBus, TBus>());
        services.AddHostedService<PermissaoInvalidacaoSubscriber>();
        return services;
    }

    /// <summary>Variante que aceita uma instância já criada do bus (ex.: um cliente Redis configurado manualmente).</summary>
    public static IServiceCollection AddPermissaoInvalidationBus(this IServiceCollection services, IPermissaoInvalidationBus bus)
    {
        services.Replace(ServiceDescriptor.Singleton(bus));
        services.AddHostedService<PermissaoInvalidacaoSubscriber>();
        return services;
    }
}
