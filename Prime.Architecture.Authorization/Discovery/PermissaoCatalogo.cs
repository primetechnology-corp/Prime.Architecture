using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Prime.Architecture.Authorization.Attributes;

namespace Prime.Architecture.Authorization.Discovery;

/// <summary>
/// Descobre as permissões do sistema: atributos <see cref="RequerPermissaoAttribute"/> (classes e métodos),
/// classes marcadas com <see cref="CatalogoDePermissoesAttribute"/> e endpoints registrados (inclui Minimal API).
/// Útil para o seed da tabela de permissões.
/// </summary>
public static class PermissaoCatalogo
{
    private static readonly string[] AcoesCrud = { "create", "read", "update", "delete" };

    /// <summary>Permissões declaradas por atributos e catálogos de constantes nos assemblies.</summary>
    public static IReadOnlyList<string> Descobrir(params Assembly[] assemblies)
    {
        var siglas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            foreach (var attr in type.GetCustomAttributes<RequerPermissaoAttribute>())
                siglas.Add(RequerPermissaoAttribute.Sigla(attr.Recurso, attr.Acao));

            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                foreach (var attr in method.GetCustomAttributes<RequerPermissaoAttribute>())
                    siglas.Add(RequerPermissaoAttribute.Sigla(attr.Recurso, attr.Acao));

            if (type.GetCustomAttribute<CatalogoDePermissoesAttribute>() != null)
                foreach (var sigla in LerConstantes(type))
                    siglas.Add(sigla);
        }

        return Ordenar(siglas);
    }

    /// <summary>
    /// Permissões dos endpoints registrados (controllers e Minimal API), incluindo policies "perm:recurso:acao"
    /// escritas à mão. Chame depois de mapear as rotas.
    /// </summary>
    public static IReadOnlyList<string> Descobrir(this IEndpointRouteBuilder app, params Assembly[] assemblies)
    {
        var siglas = new HashSet<string>(Descobrir(assemblies), StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in app.DataSources.SelectMany(d => d.Endpoints))
        {
            foreach (var dado in endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>())
            {
                if (dado is RequerPermissaoAttribute attr)
                {
                    siglas.Add(RequerPermissaoAttribute.Sigla(attr.Recurso, attr.Acao));
                    continue;
                }

                var policy = dado.Policy;
                if (policy != null && policy.StartsWith(RequerPermissaoAttribute.PrefixoPadrao, StringComparison.Ordinal))
                {
                    var sigla = policy.Substring(RequerPermissaoAttribute.PrefixoPadrao.Length).ToLowerInvariant();
                    var partes = sigla.Split(':');
                    if (partes.Length == 2 && partes[0].Length > 0 && partes[1].Length > 0)
                        siglas.Add(sigla);
                }
            }
        }

        return Ordenar(siglas);
    }

    /// <summary>Gera as quatro permissões CRUD padrão de um recurso.</summary>
    public static IEnumerable<string> Crud(string recurso)
        => AcoesCrud.Select(a => RequerPermissaoAttribute.Sigla(recurso, a));

    private static IEnumerable<string> LerConstantes(Type type)
    {
        foreach (var campo in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string)))
        {
            var valor = (string?)campo.GetRawConstantValue();
            var partes = valor?.Split(':');
            if (partes is not { Length: 2 })
                throw new ArgumentException($"Constante {type.Name}.{campo.Name} deve ter o formato \"recurso:acao\" (valor: \"{valor}\").");

            yield return RequerPermissaoAttribute.Sigla(partes[0], partes[1]);
        }

        foreach (var aninhado in type.GetNestedTypes(BindingFlags.Public))
            foreach (var sigla in LerConstantes(aninhado))
                yield return sigla;
    }

    private static IReadOnlyList<string> Ordenar(IEnumerable<string> siglas)
        => siglas.OrderBy(s => s, StringComparer.Ordinal).ToList();
}
