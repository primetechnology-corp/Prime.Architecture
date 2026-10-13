using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.EntityFramework.Configuration;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>
/// Lê usuário e perfil das claims do token. MENOS SEGURO: mudanças de perfil e bloqueios só valem quando o token expira.
/// Exige <see cref="EfPermissaoOptions.ConfiarNoPerfilDoToken"/> explícito.
/// </summary>
public class ClaimsUsuarioPerfilProvider : IUsuarioPerfilProvider
{
    private readonly EfPermissaoOptions _options;

    public ClaimsUsuarioPerfilProvider(IOptions<EfPermissaoOptions> options)
    {
        _options = options.Value;
    }

    public Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        UsuarioPerfilInfo? info = null;

        if (int.TryParse(user.FindFirstValue(_options.ClaimUsuario), out var usuarioId) &&
            int.TryParse(user.FindFirstValue(_options.ClaimPerfil), out var perfilId))
            info = new UsuarioPerfilInfo(usuarioId, perfilId);

        return Task.FromResult(info);
    }
}
