using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace MessageHub;

// The read side of the inbox. SignalR only nudges; everything the client renders comes from
// here, so there is exactly one shape and one code path producing notifications.
//
// userId comes from the query string, matching the shortcut already taken in Negotiate.cs.
// A real deployment derives it from an authenticated claim -- same caveat, same place to fix.
public class Notifications
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly INotificationStore _store;
    private readonly ILogger<Notifications> _logger;

    public Notifications(INotificationStore store, ILogger<Notifications> logger)
    {
        _store = store;
        _logger = logger;
    }

    [Function(nameof(GetNotifications))]
    public async Task<HttpResponseData> GetNotifications(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var userId = query["userId"];

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await Problem(req, HttpStatusCode.BadRequest, "userId is required.");
        }

        var unreadOnly = string.Equals(query["unread"], "true", StringComparison.OrdinalIgnoreCase);

        var documents = await _store.GetForUserAsync(userId, unreadOnly, cancellationToken);
        var payload = documents.Select(d => d.ToResponse()).ToArray();

        _logger.LogInformation(
            "Returned {count} notification(s) for {userId} (unreadOnly={unreadOnly}).",
            payload.Length, userId, unreadOnly);

        return await Json(req, HttpStatusCode.OK, payload);
    }

    // userId is required because it *is* the partition key -- without it this could not be a
    // point write, and a caller could not be confined to their own notifications.
    [Function(nameof(MarkNotificationRead))]
    public async Task<HttpResponseData> MarkNotificationRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/{id}/read")] HttpRequestData req,
        string id,
        CancellationToken cancellationToken)
    {
        var userId = System.Web.HttpUtility.ParseQueryString(req.Url.Query)["userId"];

        if (string.IsNullOrWhiteSpace(userId))
        {
            return await Problem(req, HttpStatusCode.BadRequest, "userId is required.");
        }

        var updated = await _store.MarkReadAsync(userId, id, cancellationToken);

        if (!updated)
        {
            return await Problem(req, HttpStatusCode.NotFound, $"Notification '{id}' not found for this user.");
        }

        _logger.LogInformation("Marked notification {id} read for {userId}.", id, userId);
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    private static async Task<HttpResponseData> Json<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions));
        return response;
    }

    private static Task<HttpResponseData> Problem(HttpRequestData req, HttpStatusCode status, string message) =>
        Json(req, status, new { error = message });
}
