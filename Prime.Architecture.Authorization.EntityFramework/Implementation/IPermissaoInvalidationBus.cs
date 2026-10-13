using System;
using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Authorization.EntityFramework.Implementation;

public enum PermissaoInvalidacaoTipo
{
    Perfil,
    Usuario,

    /// <summary>Todo o cache de permissões (o <c>Id</c> da mensagem é ignorado). Publicado, por exemplo, após sincronizar permissões.</summary>
    Todos
}

/// <summary>Mensagem de invalidação de cache entre instâncias. <see cref="Origem"/> identifica a instância que publicou.</summary>
public record PermissaoInvalidacao(PermissaoInvalidacaoTipo Tipo, int Id, string Origem);

/// <summary>
/// Canal de invalidação de cache entre instâncias (ex.: Redis pub/sub, Service Bus). Implemente e registre com
/// <c>AddPermissaoInvalidationBus&lt;TBus&gt;()</c>. Sem um bus, a invalidação é apenas local.
/// </summary>
public interface IPermissaoInvalidationBus
{
    Task PublicarAsync(PermissaoInvalidacao mensagem, CancellationToken cancellationToken = default);

    /// <summary>Passa a receber as mensagens (inclusive as publicadas por outras instâncias). Descarte para cancelar.</summary>
    IDisposable Assinar(Func<PermissaoInvalidacao, Task> tratador);
}
