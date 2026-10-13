using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Prime.Architecture.Authorization.EntityFramework.Configuration;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>
/// Coloca cache (com proteção contra stampede) em volta do <see cref="IUsuarioPerfilProvider"/> da aplicação,
/// evitando uma consulta de usuário/perfil por requisição. Invalide com <c>InvalidarUsuarioAsync</c> ao mudar o perfil
/// ou bloquear o usuário. A carga compartilhada não usa o CancellationToken de nenhuma requisição específica.
/// </summary>
internal sealed class CachedUsuarioPerfilProvider : IUsuarioPerfilProvider
{
    private readonly IUsuarioPerfilProvider _inner;
    private readonly SingleFlightCache _cache;
    private readonly EfPermissaoOptions _options;

    public CachedUsuarioPerfilProvider(IUsuarioPerfilProvider inner, SingleFlightCache cache, IOptions<EfPermissaoOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<UsuarioPerfilInfo?> ObterAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        // Sem id utilizável na claim, não há chave segura: consulta direto
        if (_options.DuracaoCachePerfilUsuario <= System.TimeSpan.Zero ||
            !int.TryParse(user.FindFirstValue(_options.ClaimUsuario), out var usuarioId))
            return await _inner.ObterAsync(user, cancellationToken);

        var key = CacheKeys.UsuarioPerfil(usuarioId);
        var info = await _cache.GetOrCreateAsync(key, _options.DuracaoCachePerfilUsuario,
            () => _inner.ObterAsync(user, CancellationToken.None));

        // Defesa: o cache é indexado pela claim configurada; se o provider resolveu OUTRO usuário, não confie no cache
        if (info != null && info.UsuarioId != usuarioId)
        {
            _cache.Invalidar(key);
            return await _inner.ObterAsync(user, cancellationToken);
        }

        return info;
    }
}
