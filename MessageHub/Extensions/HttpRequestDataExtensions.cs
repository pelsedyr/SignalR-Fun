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
    /// Async on purpose. The sibling Avdekl function apps write the body with the synchronous
    /// WriteString, which is fine under their HostBuilder model — but this app uses
    /// ConfigureFunctionsWebApplication() (ASP.NET Core-integrated), where synchronous IO throws
    /// "Synchronous operations are disallowed". Same class of difference the comment in
    /// NegotiateController describes.
    /// </summary>
    public static async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        this HttpRequestData req, HttpStatusCode statusCode, T content)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", MediaTypeNames.Application.Json);
        await response.WriteStringAsync(JsonSerializer.Serialize(content, JsonOptions));

        return response;
    }

    public static Task<HttpResponseData> CreateProblemResponseAsync(
        this HttpRequestData req, HttpStatusCode statusCode, string message) =>
        req.CreateJsonResponseAsync(statusCode, new { error = message });
}
