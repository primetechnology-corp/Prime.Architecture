using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Configuration;
using Prime.Architecture.Authorization.Interfaces;

namespace Prime.Architecture.Authorization.Implementation;

/// <summary>
/// Resolver padrão: lê as permissões das claims do usuário autenticado.
/// </summary>
public class ClaimsPermissaoResolver : IPermissaoResolver
{
    private readonly ResourceAuthorizationOptions _options;

    public ClaimsPermissaoResolver(IOptions<ResourceAuthorizationOptions> options)
    {
        _options = options.Value;
    }

    public Task<IReadOnlySet<string>> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        IReadOnlySet<string> permissoes = user
            .FindAll(_options.ClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Task.FromResult(permissoes);
    }
}
