using System.Threading;
using System.Threading.Tasks;

namespace Prime.Architecture.Mediator.Standard.Notifications;

public interface INotificationHandler<TNotification> where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken = default);
}
