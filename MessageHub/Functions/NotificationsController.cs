using System.Net;
using MessageHub.Extensions;
using MessageHub.Extensions.Logger;
using MessageHub.Repositories;
using MessageHub.Static;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MessageHub.Functions;

/// <summary>
/// The read side of the inbox. SignalR only nudges; everything the client renders comes from
/// here, so there is exactly one shape and one code path producing notifications.
///
/// userId comes from the query string, matching the shortcut taken in NegotiateController.
/// A real deployment derives it from an authenticated claim — same caveat, same place to fix.
/// </summary>
public class NotificationsController
{
    private readonly INotificationRepository _notificationRepository;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationRepository notificationRepository,
        ILogger<NotificationsController> logger)
    {
        _notificationRepository = notificationRepository;
        _logger = logger;
    }

    [Function(nameof(GetNotifications))]
    public async Task<HttpResponseData> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (!req.TryGetUserId(out var userId))
            return await req.CreateProblemResponseAsync(HttpStatusCode.BadRequest, ExceptionMessages.Http.MissingUserId);

        var unreadOnly = string.Equals(
            req.GetQueryValue(Strings.QueryParameters.Unread), "true", StringComparison.OrdinalIgnoreCase);

        var documents = await _notificationRepository.GetForReceiverAsync(userId, unreadOnly, cancellationToken);
        var payload = documents.Select(document => document.ToResponse()).ToArray();

        _logger.LogNotificationsReturned(payload.Length, userId, unreadOnly);

        return await req.CreateJsonResponseAsync(HttpStatusCode.OK, payload);
    }

    /// <summary>
    /// userId is required because it *is* the partition key — without it this could not be a
    /// point write, and a caller could not be confined to their own notifications.
    /// </summary>
    [Function(nameof(MarkNotificationRead))]
    public async Task<HttpResponseData> MarkNotificationRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/{id}/read")] HttpRequestData req,
        string id,
        CancellationToken cancellationToken)
    {
        if (!req.TryGetUserId(out var userId))
            return await req.CreateProblemResponseAsync(HttpStatusCode.BadRequest, ExceptionMessages.Http.MissingUserId);

        var updated = await _notificationRepository.MarkReadAsync(userId, id, cancellationToken);

        if (!updated)
        {
            _logger.LogNotificationNotFound(id, userId);
            return await req.CreateProblemResponseAsync(
                HttpStatusCode.NotFound,
                string.Format(ExceptionMessages.Http.NotificationNotFound, id));
        }

        _logger.LogNotificationMarkedRead(id, userId);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
