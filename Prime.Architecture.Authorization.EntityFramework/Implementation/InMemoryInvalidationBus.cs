using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>
/// Bus em memória do processo: serve para testes e para vários hosts no mesmo processo.
/// Para várias máquinas, implemente <see cref="IPermissaoInvalidationBus"/> com Redis ou similar.
/// </summary>
public sealed class InMemoryInvalidationBus : IPermissaoInvalidationBus
{
    private readonly ConcurrentDictionary<Guid, Func<PermissaoInvalidacao, Task>> _assinantes = new();

    public async Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken cancellationToken = default)
    {
        foreach (var tratador in _assinantes.Values.ToList())
            await tratador(mensagem);
    }

    public IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador)
    {
        var id = Guid.NewGuid();
        _assinantes[id] = tratador;
        return new Assinatura(() => _assinantes.TryRemove(id, out _));
    }

    private sealed class Assinatura(Action aoDescartar) : IDisposable
    {
        public void Dispose() => aoDescartar();
    }
}
