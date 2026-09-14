using System.Net;
using System.Net.Mime;
using System.Text.Json;
using System.Web;
using MessageHub.Static;
using Microsoft.Azure.Functions.Worker.Http;

namespace MessageHub.Extensions;

public static class HttpRequestDataExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? GetQueryValue(this HttpRequestData req, string name) =>
        HttpUtility.ParseQueryString(req.Url.Query)[name];

    public static bool TryGetUserId(this HttpRequestData req, out string userId)
    {
        userId = req.GetQueryValue(Strings.QueryParameters.UserId) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(userId);
    }

    /// <summary>
    /// Returns null for an absent, empty or malformed body so the caller can turn that into a
    /// 400. Without the catch, a hand-rolled curl with a typo would surface as a 500 for what
    /// is plainly a client error.
    ///
    /// JsonSerializerDefaults.Web gives PropertyNameCaseInsensitive, so the browser's
    /// {"receiverId":...} binds to a record's PascalCase positional constructor. Note this is
    /// the *inbound HTTP* convention only — the Service Bus wire format is PascalCase and
    /// case-sensitive, see NotificationPublisher.
    /// </summary>
    public static async Task<T?> ReadJsonBodyAsync<T>(
        this HttpRequestData req, CancellationToken cancellationToken) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Async on purpose. The sibling Avdekl function apps write the body with the synchronous
    /// WriteString, which is fine under their HostBuilder model — but this app uses
    /// ConfigureFunctionsWebApplication() (ASP.NET Core-integrated), where synchronous IO throws
    /// "Synchronous operations are disallowed". Same class of difference the comment in
    /// NegotiateController describes.
    /// </summary>
    /// <param name="configureHeaders">
    /// Runs before the body is written, and that ordering is not optional: under
    /// ConfigureFunctionsWebApplication() WriteStringAsync starts the response, after which
    /// added headers are dropped *silently* — no exception, the header simply never arrives.
    /// Anything beyond Content-Type has to go through here.
    /// </param>
    public static async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        this HttpRequestData req, HttpStatusCode statusCode, T content,
        Action<HttpResponseData>? configureHeaders = null)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", MediaTypeNames.Application.Json);
        configureHeaders?.Invoke(response);
        await response.WriteStringAsync(JsonSerializer.Serialize(content, JsonOptions));

        return response;
    }

    public static Task<HttpResponseData> CreateProblemResponseAsync(
        this HttpRequestData req, HttpStatusCode statusCode, string message,
        Action<HttpResponseData>? configureHeaders = null) =>
        req.CreateJsonResponseAsync(statusCode, new { error = message }, configureHeaders);
}
