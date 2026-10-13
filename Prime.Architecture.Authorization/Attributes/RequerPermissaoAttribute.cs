using System;
using Microsoft.AspNetCore.Authorization;

namespace Prime.Architecture.Authorization.Attributes;

/// <summary>
/// Exige a permissão "recurso:acao" para acessar o endpoint, em controllers, actions ou Minimal APIs.
/// Não depende do nome do controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequerPermissaoAttribute : AuthorizeAttribute
{
    /// <summary>Prefixo dos nomes de policy geradas dinamicamente.</summary>
    public const string PrefixoPadrao = "perm:";

    public string Recurso { get; }
    public string Acao { get; }

    public RequerPermissaoAttribute(string recurso, string acao)
        : base(PrefixoPadrao + Sigla(recurso, acao))
    {
        Recurso = recurso;
        Acao = acao;
    }

    /// <summary>
    /// Monta a sigla normalizada (minúscula): "recurso:acao".
    /// Recurso e ação não podem ser vazios nem conter ':' (evita siglas ambíguas como "a:b" + "c" e "a" + "b:c").
    /// </summary>
    public static string Sigla(string recurso, string acao)
    {
        Validar(recurso, nameof(recurso));
        Validar(acao, nameof(acao));
        return $"{recurso.Trim().ToLowerInvariant()}:{acao.Trim().ToLowerInvariant()}";
    }

    private static void Validar(string valor, string nome)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new ArgumentException("Não pode ser vazio.", nome);

        if (valor.IndexOf(':') >= 0)
            throw new ArgumentException("Não pode conter ':'.", nome);
    }
}
