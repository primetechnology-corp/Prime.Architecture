using Microsoft.AspNetCore.Authorization;

namespace Prime.Architecture.Authorization.Requirements;

/// <summary>
/// Exige que o usuário possua a permissão informada (sigla "recurso:acao").
/// </summary>
public sealed class PermissaoRequirement : IAuthorizationRequirement
{
    public PermissaoRequirement(string permissao)
    {
        Permissao = permissao;
    }

    public string Permissao { get; }
}
