using System.Net.Mime;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MessageHub.Dto;
using MessageHub.Exceptions;
using MessageHub.Extensions.Logger;
using MessageHub.Static;
using Microsoft.Extensions.Logging;

namespace MessageHub.Publishers;

/// <summary>
/// The send side of the queue. This exists as an SDK sender rather than a [ServiceBusOutput]
/// binding for two reasons the binding cannot work around:
///
/// 1. MessageId is unreachable through the binding. The isolated model has no
///    ServiceBusMessage output type — only string, byte[] and JSON-serializable POCOs — so a
///    bound return arrives at the broker with MessageId null, and every send would fall into
///    NotificationIds' sb-{SequenceNumber} fallback. That still survives redelivery, but it
///    loses the double-submit collision and puts a second id shape in Cosmos next to
///    cli/ServiceBusPostTool's GUIDs.
/// 2. Output bindings run *after* the function returns, so a failed send would still be
///    answered with 202. An awaited send lets SendController answer 503 truthfully.
/// </summary>
public class NotificationPublisher : INotificationPublisher
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(ServiceBusSender sender, ILogger<NotificationPublisher> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task<string> PublishAsync(NotificationDto notification, CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid().ToString();

        // Serialized with no options on purpose. cli/ServiceBusPostTool uses
        // JsonSerializer.Serialize(notification) — default options, so PascalCase — and
        // ServiceBusQueueTrigger deserializes with defaults too, which are case-*sensitive*.
        // "Improving" this to camelCase here would make the trigger read nulls.
        var message = new ServiceBusMessage(JsonSerializer.Serialize(notification))
        {
            MessageId = messageId,
            ContentType = MediaTypeNames.Application.Json,
            // Same three properties, same values, as cli/ServiceBusPostTool: one wire format,
            // two producers, indistinguishable downstream.
            Subject = nameof(NotificationDto),
        };

        try
        {
            await _sender.SendMessageAsync(message, cancellationToken);
        }
        catch (ServiceBusException ex)
        {
            _logger.LogEnqueueFailed(messageId, notification.ReceiverId, _sender.EntityPath, ex.Reason.ToString(), ex);
            throw new NotificationException(
                string.Format(ExceptionMessages.Notifications.EnqueueFailed, notification.ReceiverId), ex);
        }

        _logger.LogMessageEnqueued(messageId, notification.ReceiverId, _sender.EntityPath);
        return messageId;
    }
}
