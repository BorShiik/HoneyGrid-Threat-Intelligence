using HoneyGrid.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Rozsyłanie zdarzeń na żywo do dashboardu (Track B, realtime).
///
/// Wyzwalacz Change Feed na kontenerze <c>events</c> → komunikat SignalR
/// (Serverless output binding) do huba <c>attacks</c>, metoda <c>attack</c>.
/// Front (HoneyGrid.Web/src/api/signalr.ts) nasłuchuje dokładnie tej nazwy.
///
/// Osobny prefiks dzierżaw ("fanout") — nie koliduje z ClassifyEvents, który
/// czyta ten sam Change Feed pod prefiksem "classify".
///
/// Uwaga: PATCH klasyfikacji również generuje zmianę w Change Feed, więc każde
/// zdarzenie może zostać rozesłane dwukrotnie (na wstawienie i po klasyfikacji
/// — za drugim razem z polem classification). Klient może deduplikować po
/// <c>id</c>; dla demo to akceptowalne (drugi push wzbogaca dane na mapie).
/// </summary>
public sealed class FanOutToSignalR
{
    private const string HubName = "attacks";
    private const string Target = "attack";

    private readonly ILogger<FanOutToSignalR> _logger;

    public FanOutToSignalR(ILogger<FanOutToSignalR> logger) => _logger = logger;

    [Function(nameof(FanOutToSignalR))]
    [SignalROutput(HubName = HubName)]
    public SignalRMessageAction[] Run(
        [CosmosDBTrigger(
            databaseName: "%CosmosDatabase%",
            containerName: "events",
            Connection = "CosmosConnection",
            LeaseContainerName = "leases",
            LeaseContainerPrefix = "fanout",
            CreateLeaseContainerIfNotExists = true)]
        IReadOnlyList<HoneypotEvent> changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("FanOut: rozsyłam {Count} zdarzeń do huba '{Hub}'.", changes.Count, HubName);

        return changes
            .Select(evt => new SignalRMessageAction(Target, [evt]))
            .ToArray();
    }
}
