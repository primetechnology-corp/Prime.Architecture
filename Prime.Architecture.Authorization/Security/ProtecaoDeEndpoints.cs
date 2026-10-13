using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Prime.Architecture.Authorization.Security;

public static class ProtecaoDeEndpoints
{
    /// <summary>
    /// Falha a inicialização se algum endpoint não declarar autorização (nem [Authorize]/[RequerPermissao]
    /// nem [AllowAnonymous]). Chame depois de mapear todas as rotas (<c>MapControllers</c>, <c>MapGet</c>...).
    /// Use <paramref name="ignorar"/> para excluir rotas técnicas (swagger, health checks).
    /// </summary>
    /// <exception cref="InvalidOperationException">Há endpoints sem declaração explícita de autorização.</exception>
    public static void ExigirEndpointsProtegidos(this IEndpointRouteBuilder app, Func<RouteEndpoint, bool>? ignorar = null)
    {
        var semDeclaracao = app.DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => ignorar?.Invoke(e) != true)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() == null &&
                        !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.DisplayName ?? e.RoutePattern.RawText ?? "(sem nome)")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (semDeclaracao.Count > 0)
            throw new InvalidOperationException(
                "Endpoints sem autorização declarada (use [RequerPermissao], [Authorize] ou [AllowAnonymous]):" +
                Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", semDeclaracao));
    }
}
