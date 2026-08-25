using System.Text.Json.Serialization;

namespace MessageHub.Dto;

/// <summary>
/// What goes over SignalR. Deliberately carries no Content: including it would be enough to
/// render without fetching, which reintroduces the second shape this design avoids.
///
/// Explicit names because the SignalR output binding serializes with .NET defaults
/// (PascalCase), which would otherwise disagree with the camelCase the REST API returns.
/// </summary>
public record NotificationNudge(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc);
