using Microsoft.AspNetCore.Builder;
using Prime.Architecture.Authorization.Attributes;

namespace Prime.Architecture.Authorization.Extensions;

public static class EndpointExtensions
{
    /// <summary>
    /// Exige a permissão "recurso:acao" em um endpoint (Minimal API ou grupo de rotas).
    /// Diferente de <c>RequireAuthorization("perm:...")</c> escrito à mão, esta forma é encontrada
    /// pela descoberta de permissões.
    /// </summary>
    public static TBuilder RequerPermissao<TBuilder>(this TBuilder builder, string recurso, string acao)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(new RequerPermissaoAttribute(recurso, acao));
}
