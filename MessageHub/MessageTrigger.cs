using System;
using System.Text.Json;
using System.Threading.Tasks;
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
    private readonly ILogger<MessageTrigger> _logger;

    public MessageTrigger(ILogger<MessageTrigger> logger)
    {
        _logger = logger;
    }

    [Function(nameof(MessageTrigger))]
    public async Task<NotificationOutputs> Run(
        [ServiceBusTrigger("%Email:ServiceBus:DefaultQueue%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation("Message ID: {id}", message.MessageId);
        _logger.LogInformation("Message Body: {body}", message.Body);
        _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

        var notification = JsonSerializer.Deserialize<NotificationDto>(message.Body.ToString());

        // Complete the message
        await messageActions.CompleteMessageAsync(message);

        if (notification is null)
        {
            _logger.LogWarning("Message ID {id} body did not deserialize to a NotificationDto; skipping SignalR push.", message.MessageId);
            return new NotificationOutputs();
        }

        return new NotificationOutputs
        {
            Notification = new SignalRMessageAction("notificationReceived")
            {
                Arguments = new object[] { message.Body.ToString() },
                UserId = notification.ReceiverId
            }
        };
    }
}
