using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Endpoint negocjacji SignalR (tryb Serverless) — Track B, realtime.
///
/// Klient dashboardu (HoneyGrid.Web) łączy się przez
/// <c>withUrl("https://&lt;funcapp&gt;/api/hubs/attacks")</c>; biblioteka SignalR
/// dokłada do tego <c>/negotiate</c> i trafia tutaj. Funkcja zwraca dane
/// połączenia (URL usługi SignalR + krótkotrwały token dostępu) z wiązania
/// <see cref="SignalRConnectionInfoInputAttribute"/>.
///
/// Połączenie z usługą SignalR jest bezkluczowe: ustaw
/// <c>AzureSignalRConnectionString__serviceUri</c> i nadaj tożsamości aplikacji
/// rolę „SignalR Service Owner". To samo wiązanie/po­łączenie wykorzystuje
/// <see cref="FanOutToSignalR"/> przy rozsyłaniu zdarzeń.
/// </summary>
public sealed class NegotiateSignalR
{
    [Function("negotiate")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "hubs/attacks/negotiate")]
        HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "attacks")] string connectionInfo)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(connectionInfo);
        return response;
    }
}
