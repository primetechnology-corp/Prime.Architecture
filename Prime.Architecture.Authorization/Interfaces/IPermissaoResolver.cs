using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.Interfaces;

/// <summary>
/// Define de onde vêm as permissões (siglas "recurso:acao") de um usuário.
/// A implementação padrão lê as claims do token; substitua para consultar banco ou cache.
/// </summary>
public interface IPermissaoResolver
{
    Task<IReadOnlySet<string>> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
