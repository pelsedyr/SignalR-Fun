using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace MessageHub;

public class MessageTrigger
{
    private readonly ILogger<MessageTrigger> _logger;

    public MessageTrigger(ILogger<MessageTrigger> logger)
    {
        _logger = logger;
    }

    [Function(nameof(MessageTrigger))]
    public async Task Run(
        [ServiceBusTrigger("%Email:ServiceBus:DefaultQueue%", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        _logger.LogInformation("Message ID: {id}", message.MessageId);
        _logger.LogInformation("Message Body: {body}", message.Body);
        _logger.LogInformation("Message Content-Type: {contentType}", message.ContentType);

        // Complete the message
        await messageActions.CompleteMessageAsync(message);
    }
}