using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Mediator.HighPerformance.Notifications;

public interface INotificationHandler<TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
