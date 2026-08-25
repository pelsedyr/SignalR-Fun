using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace MessageHub;

// Isolated-worker functions can only bind one output on the method itself, so the second
// output (SignalR) is exposed as a property on a wrapper the method returns instead.
public class NotificationOutputs
{
    [SignalROutput(HubName = "%Notifications:HubName%", ConnectionStringSetting = "AzureSignalRConnectionString")]
    public SignalRMessageAction? Notification { get; set; }
}

public class MessageTrigger
{
    private readonly INotificationStore _store;
    private readonly ILogger<MessageTrigger> _logger;

    public MessageTrigger(INotificationStore store, ILogger<MessageTrigger> logger)
    {
        _store = store;
        _logger = logger;
    }

    // No ServiceBusMessageActions and no manual CompleteMessageAsync, deliberately. The host
    // processes output bindings *after* this method returns, so settling the message in here
    // would forfeit redelivery for anything that fails later -- the Cosmos write is safe, but a
    // failed SignalR push would discard the notification with nothing left to retry.
    //
    // Letting auto-completion settle the message means it is completed only once the body and
    // every output binding have succeeded. That also keeps host.json's default
    // autoCompleteMessages: true correct, which manual settlement would have conflicted with.
    [Function(nameof(MessageTrigger))]
    public async Task<NotificationOutputs> Run(
        [ServiceBusTrigger("%Email:ServiceBus:DefaultQueue%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        // Body is not logged: it is user content, and it ends up in telemetry once App Insights
        // is enabled.
        _logger.LogInformation(
            "Received message {id} ({contentType}).", message.MessageId, message.ContentType);

        // Throwing rather than completing: a malformed body will never succeed, so let it
        // exhaust MaxDeliveryCount and dead-letter, where it can be inspected. Completing it
        // here would drop it silently.
        var notification = JsonSerializer.Deserialize<NotificationDto>(message.Body.ToString())
            ?? throw new InvalidOperationException(
                $"Message {message.MessageId} did not deserialize to a {nameof(NotificationDto)}.");

        var document = new NotificationDocument
        {
            Id = NotificationIds.FromMessage(message),
            ReceiverId = notification.ReceiverId,
            Content = notification.Content,
            // Broker-assigned, UTC, millisecond precision -- better than the DTO's DateOnly,
            // and it needs no change to the contract shared with cli/ServiceBusPostTool.
            CreatedUtc = message.EnqueuedTime,
        };

        // Persist first. The SignalR nudge below is returned as an output binding, so the host
        // pushes it only after this write has succeeded.
        var created = await _store.TryCreateAsync(document, cancellationToken);

        _logger.LogInformation(
            created
                ? "Stored notification {id} for {receiver}."
                : "Notification {id} for {receiver} was already stored; nudging anyway.",
            document.Id, document.ReceiverId);

        // Nudge even on a duplicate: it is harmless (the client re-fetches the same list) and it
        // covers the case where an earlier attempt persisted but failed before pushing.
        return new NotificationOutputs
        {
            Notification = new SignalRMessageAction("notificationReceived")
            {
                Arguments = [document.ToNudge()],
                UserId = document.ReceiverId,
            },
        };
    }
}
