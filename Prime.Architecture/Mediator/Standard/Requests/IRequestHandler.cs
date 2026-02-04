using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Mediator.Standard.Requests;

public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken = default);
}
