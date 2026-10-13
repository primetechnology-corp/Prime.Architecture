using System.Text.Json;
using Prime.Architecture.Authorization.EntityFramework.Implementation;
using StackExchange.Redis;

namespace Prime.Architecture.Authorization.Examples.Custom;

/// <summary>
/// Invalidação de cache entre instâncias via Redis pub/sub. Proteja o Redis (senha/TLS, rede privada):
/// quem consegue publicar no canal consegue forçar a limpeza do cache (não concede acesso, mas pode degradar o desempenho).
/// </summary>
public sealed class RedisInvalidationBus(IConnectionMultiplexer redis) : IPermissaoInvalidationBus
{
    private static readonly RedisChannel Canal = RedisChannel.Literal("prime:permissoes:invalidacao");

    public Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken cancellationToken = default)
        => redis.GetSubscriber().PublishAsync(Canal, JsonSerializer.Serialize(mensagem));

    public IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador)
    {
        var subscriber = redis.GetSubscriber();
        subscriber.Subscribe(Canal, (canal, valor) =>
        {
            var mensagem = JsonSerializer.Deserialize<PermissaoInvalidacao>(valor.ToString());
            if (mensagem != null)
                _ = tratador(mensagem);
        });

        return new Descartavel(() => subscriber.Unsubscribe(Canal));
    }

    private sealed class Descartavel(Action aoDescartar) : IDisposable
    {
        public void Dispose() => aoDescartar();
    }
}
