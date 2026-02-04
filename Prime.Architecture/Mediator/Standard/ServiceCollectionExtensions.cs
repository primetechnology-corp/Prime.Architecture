using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Prime.Architecture.Mediator.Standard.Notifications;
using Prime.Architecture.Mediator.Standard.Pipeline;
using Prime.Architecture.Mediator.Standard.Requests;

namespace Prime.Architecture.Mediator.Standard;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMediatorStandard(this IServiceCollection services, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.GetInterfaces()
                    .Any(i => i.IsGenericType &&
                              (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                               i.GetGenericTypeDefinition() == typeof(INotificationHandler<>) ||
                               i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))));

            foreach (var type in handlerTypes)
            {
                foreach (var iface in type.GetInterfaces()
                             .Where(i => i.IsGenericType &&
                                         (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                                          i.GetGenericTypeDefinition() == typeof(INotificationHandler<>) ||
                                          i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))))
                {
                    services.AddTransient(iface, type);
                }
            }
        }

        services.AddScoped<Mediator>();
        return services;
    }
}
