using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Mediator.HighPerformance.Requests;

public interface IRequest<TResponse>
{
    Task<TResponse> SendTo(Mediator mediator, CancellationToken cancellationToken);
}
