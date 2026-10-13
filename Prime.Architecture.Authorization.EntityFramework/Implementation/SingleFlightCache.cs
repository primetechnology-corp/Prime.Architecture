using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

/// <summary>
/// Cache em memória com proteção contra <i>stampede</i>: requisições simultâneas para a mesma chave com cache vazio
/// compartilham uma única carga (uma consulta ao banco). Invalidar uma chave descarta também uma carga em andamento,
/// para que um valor antigo não seja gravado depois da invalidação.
/// </summary>
public sealed class SingleFlightCache
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _emAndamento = new();
    private long _geracao;

    public SingleFlightCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    // A geração faz parte da chave: InvalidarTudo a incrementa e todas as entradas anteriores deixam de ser encontradas
    // (e de ser gravadas por cargas em andamento), sem tocar no restante do IMemoryCache da aplicação.
    private string Chave(string key) => string.Concat(Volatile.Read(ref _geracao).ToString(), "|", key);

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan duracao, Func<Task<T>> carregar)
    {
        if (duracao <= TimeSpan.Zero)
            return await carregar();

        key = Chave(key);

        if (_cache.TryGetValue(key, out Box<T>? existente) && existente != null)
            return existente.Valor;

        Lazy<Task<object?>>? criada = null;
        criada = new Lazy<Task<object?>>(() => CarregarAsync(key, duracao, carregar, () => criada!));
        var lazy = _emAndamento.GetOrAdd(key, criada);

        return (T)(await lazy.Value)!;
    }

    public void Invalidar(string key)
    {
        key = Chave(key);
        _emAndamento.TryRemove(key, out _);
        _cache.Remove(key);
    }

    /// <summary>
    /// Invalida todas as entradas deste cache (e descarta cargas em andamento), sem afetar outras chaves do
    /// <see cref="IMemoryCache"/>. As entradas antigas ficam inacessíveis e expiram sozinhas.
    /// </summary>
    public void InvalidarTudo() => Interlocked.Increment(ref _geracao);

    private async Task<object?> CarregarAsync<T>(string key, TimeSpan duracao, Func<Task<T>> carregar, Func<Lazy<Task<object?>>> propria)
    {
        var minha = propria();
        try
        {
            var valor = await carregar();

            // Só grava se esta carga ainda é a vigente (não foi invalidada enquanto carregava)
            if (_emAndamento.TryGetValue(key, out var atual) && ReferenceEquals(atual, minha))
                _cache.Set(key, new Box<T>(valor), duracao);

            return valor;
        }
        finally
        {
            _emAndamento.TryRemove(new System.Collections.Generic.KeyValuePair<string, Lazy<Task<object?>>>(key, minha));
        }
    }

    private sealed class Box<T>(T valor)
    {
        public T Valor { get; } = valor;
    }
}
