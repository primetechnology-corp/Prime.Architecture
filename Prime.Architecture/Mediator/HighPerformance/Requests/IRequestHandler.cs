using System.Threading;
using System.Threading.Tasks;
using Prime.Architecture.Mediator.HighPerformance.Requests;

namespace Prime.Architecture.Mediator.HighPerformance.Requests;

public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}
