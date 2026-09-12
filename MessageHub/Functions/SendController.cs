using System.Net;
using MessageHub.Dto;
using MessageHub.Exceptions;
using MessageHub.Extensions;
using MessageHub.Extensions.Logger;
using MessageHub.Publishers;
using MessageHub.Static;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MessageHub.Functions;

/// <summary>
/// The write side. A browser cannot talk to Service Bus directly — that would mean shipping
/// the connection string into client-side JavaScript — so this endpoint is the intermediary:
/// it validates, then puts a message on the same queue cli/ServiceBusPostTool posts to.
/// Everything after that (Cosmos write, SignalR nudge) is ServiceBusQueueTrigger's, unchanged.
/// One code path creates notifications; this one only asks for one.
///
/// Anonymous, and receiverId is whatever the caller says — so anyone can notify anyone. Same
/// shortcut as NegotiateController, and materially more consequential here. Same place to fix.
///
/// The read side of this route lives in NotificationsController.
/// </summary>
public class SendController
{
    /// <summary>How long a client should wait before retrying a send the bus refused.</summary>
    private const string RetryAfterSeconds = "5";

    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<SendController> _logger;

    public SendController(INotificationPublisher notificationPublisher, ILogger<SendController> logger)
    {
        _notificationPublisher = notificationPublisher;
        _logger = logger;
    }

    /// <summary>
    /// 202 Accepted, not 201 Created: the notification does not exist yet. It exists once the
    /// queue trigger has written it, a moment later — which is why the caller must not POST
    /// and then immediately expect GET /api/notifications to show it.
    /// </summary>
    [Function(nameof(SendNotification))]
    public async Task<HttpResponseData> SendNotification(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var request = await req.ReadJsonBodyAsync<SendNotificationRequest>(cancellationToken);

        if (request is null)
            return await RejectAsync(req, ExceptionMessages.Http.InvalidRequestBody);

        // Trimming is load-bearing, not cosmetic: receiverId is the Cosmos partition key *and*
        // the SignalR UserId, so "bursdag " would silently create a second inbox that the
        // receiver app can never see.
        var receiverId = request.ReceiverId?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(receiverId))
            return await RejectAsync(req, ExceptionMessages.Http.MissingReceiverId);

        if (receiverId.Length > Strings.Notifications.MaxReceiverIdLength)
            return await RejectAsync(req, string.Format(
                ExceptionMessages.Http.ReceiverIdTooLong, Strings.Notifications.MaxReceiverIdLength));

        var content = request.Content?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(content))
            return await RejectAsync(req, ExceptionMessages.Http.MissingContent);

        if (content.Length > Strings.Notifications.MaxContentLength)
            return await RejectAsync(req, string.Format(
                ExceptionMessages.Http.ContentTooLong, Strings.Notifications.MaxContentLength));

        try
        {
            var id = await _notificationPublisher.PublishAsync(
                new NotificationDto(receiverId, content), cancellationToken);

            return await req.CreateJsonResponseAsync(
                HttpStatusCode.Accepted, new SendNotificationResponse(id, receiverId));
        }
        catch (NotificationException)
        {
            // The request was fine; the bus wasn't. 503 rather than 500, and a Retry-After so
            // a client knows this is worth repeating. The publisher already logged the cause.
            //
            // The header goes through configureHeaders because it has to be set before the
            // body is written — see CreateJsonResponseAsync.
            return await req.CreateProblemResponseAsync(
                HttpStatusCode.ServiceUnavailable,
                ExceptionMessages.Http.SendUnavailable,
                response => response.Headers.Add("Retry-After", RetryAfterSeconds));
        }
    }

    private async Task<HttpResponseData> RejectAsync(HttpRequestData req, string reason)
    {
        _logger.LogSendRequestRejected(reason);
        return await req.CreateProblemResponseAsync(HttpStatusCode.BadRequest, reason);
    }
}
