using System.Text.Json;
using MessageHub.Dto;
using MessageHub.Exceptions;
using MessageHub.Extensions;
using MessageHub.Extensions.Logger;
using MessageHub.Models;
using MessageHub.Repositories;
using MessageHub.Static;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace MessageHub.Functions;

public class ServiceBusQueueTrigger
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ILogger<ServiceBusQueueTrigger> _logger;

    public ServiceBusQueueTrigger(
        INotificationRepository notificationRepository,
        ILogger<ServiceBusQueueTrigger> logger)
    {
        _notificationRepository = notificationRepository;
        _logger = logger;
    }

    /// <summary>
    /// No ServiceBusMessageActions and no manual CompleteMessageAsync, deliberately. The host
    /// processes output bindings *after* this method returns, so settling the message in here
    /// would forfeit redelivery for anything that fails later — the Cosmos write is safe, but a
    /// failed SignalR push would discard the notification with nothing left to retry.
    ///
    /// Letting auto-completion settle the message means it is completed only once the body and
    /// every output binding have succeeded. That also keeps host.json's default
    /// autoCompleteMessages: true correct, which manual settlement would have conflicted with.
    /// </summary>
    [Function(nameof(ServiceBusQueueTrigger))]
    public async Task<NotificationOutputs> NotificationQueueTriggerAsync(
        [ServiceBusTrigger(Strings.BindingExpressions.DefaultQueue,
            Connection = Strings.BindingExpressions.ServiceBusConnection)]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        _logger.LogMessageReceived(message.MessageId, message.ContentType);

        // Throwing rather than completing: a malformed body will never succeed, so let it
        // exhaust MaxDeliveryCount and dead-letter, where it can be inspected. Completing it
        // here would drop it silently.
        var notification = JsonSerializer.Deserialize<NotificationDto>(message.Body.ToString())
            ?? throw new NotificationException(
                string.Format(ExceptionMessages.Notifications.DeserializationFailed, message.MessageId));

        var document = new NotificationDocument
        {
            Id = message.ToNotificationId(),
            ReceiverId = notification.ReceiverId,
            Content = notification.Content,
            // Broker-assigned, UTC, millisecond precision — better than the DTO's DateOnly,
            // and it needs no change to the contract shared with cli/ServiceBusPostTool.
            CreatedUtc = message.EnqueuedTime,
        };

        // Persist first. The SignalR nudge below is returned as an output binding, so the host
        // pushes it only after this write has succeeded.
        var created = await _notificationRepository.TryCreateAsync(document, cancellationToken);

        if (created)
            _logger.LogNotificationStored(document.Id, document.ReceiverId);
        else
            _logger.LogNotificationAlreadyStored(document.Id, document.ReceiverId);

        // Nudge even on a duplicate: it is harmless (the client re-fetches the same list) and it
        // covers the case where an earlier attempt persisted but failed before pushing.
        return new NotificationOutputs
        {
            Notification = new SignalRMessageAction(Strings.SignalR.NotificationReceived)
            {
                Arguments = [document.ToNudge()],
                UserId = document.ReceiverId,
            },
        };
    }
}
