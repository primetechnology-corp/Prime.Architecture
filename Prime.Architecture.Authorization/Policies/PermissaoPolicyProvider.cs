using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.Attributes;
using Prime.Architecture.Authorization.Requirements;

namespace Prime.Architecture.Authorization.Policies;

/// <summary>
/// Cria policies dinamicamente a partir do nome (ex.: "perm:perfil:read"), sem registro individual.
/// </summary>
public class PermissaoPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissaoPolicyProvider(IOptions<AuthorizationOptions> authorizationOptions) : base(authorizationOptions)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequerPermissaoAttribute.PrefixoPadrao, System.StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissaoRequirement(policyName.Substring(RequerPermissaoAttribute.PrefixoPadrao.Length).ToLowerInvariant()))
            .Build();
    }
}
