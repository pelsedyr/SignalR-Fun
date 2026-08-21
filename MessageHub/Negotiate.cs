using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace MessageHub;

public class Negotiate
{
    // Anonymous auth + a raw query-string UserId is fine for this local spike. A real
    // deployment should derive UserId from an authenticated claim instead, e.g.
    // "{headers.x-ms-client-principal-id}"
    //
    // Program.cs uses ConfigureFunctionsWebApplication() (the ASP.NET Core-integrated
    // isolated worker model). Under that model, returning a plain `string`/POCO from an
    // HTTP-triggered function isn't reliably written to the response body the way it is
    // under the older worker model — so we build the HttpResponseData explicitly instead
    // of just `return connectionInfo;`.
    //
    // connectionInfo is populated by the SignalR library
    [Function(nameof(Negotiate))]
    public static async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "%Notifications:HubName%", ConnectionStringSetting = "AzureSignalRConnectionString", UserId = "{query.userId}")]
        string connectionInfo)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(connectionInfo);
        return response;
    }
}
