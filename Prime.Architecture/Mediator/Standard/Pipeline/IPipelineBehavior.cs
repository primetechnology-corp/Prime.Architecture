using System.Threading;
using System.Threading.Tasks;
using Prime.Architecture.Mediator.Standard.Requests;

namespace Prime.Architecture.Mediator.Standard.Pipeline;

public interface IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();
