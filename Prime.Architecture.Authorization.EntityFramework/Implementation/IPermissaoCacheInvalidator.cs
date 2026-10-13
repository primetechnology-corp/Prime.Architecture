using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>Invalida o cache de permissões após alterar um perfil ou um usuário.</summary>
public interface IPermissaoCacheInvalidator
{
    /// <summary>Invalida apenas nesta instância.</summary>
    void InvalidarPerfil(int perfilId);

    /// <summary>Invalida apenas nesta instância.</summary>
    void InvalidarUsuario(int usuarioId);

    /// <summary>Invalida todo o cache de permissões nesta instância (não afeta outros itens do <c>IMemoryCache</c>).</summary>
    void InvalidarTudo();

    /// <summary>Invalida nesta instância e avisa as demais pelo <see cref="IPermissaoInvalidationBus"/> (se registrado).</summary>
    Task InvalidarPerfilAsync(int perfilId, CancellationToken cancellationToken = default);

    /// <summary>Invalida nesta instância e avisa as demais pelo <see cref="IPermissaoInvalidationBus"/> (se registrado).</summary>
    Task InvalidarUsuarioAsync(int usuarioId, CancellationToken cancellationToken = default);

    /// <summary>Invalida todo o cache nesta instância e avisa as demais pelo <see cref="IPermissaoInvalidationBus"/> (se registrado).</summary>
    Task InvalidarTudoAsync(CancellationToken cancellationToken = default);
}
