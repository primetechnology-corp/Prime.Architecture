using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.EntityFramework;

/// <summary>Usuário e perfil vigentes, conforme o banco da aplicação.</summary>
public record UsuarioPerfilInfo(int UsuarioId, int PerfilId);

/// <summary>
/// Informa o perfil VIGENTE do usuário autenticado, lido da fonte de verdade (banco), e não do token.
/// Retorne <c>null</c> quando o usuário não existir, estiver inativo ou bloqueado: ele ficará sem nenhuma permissão.
/// </summary>
public interface IUsuarioPerfilProvider
{
    Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
