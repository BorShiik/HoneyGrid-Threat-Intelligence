using System.Text.Json;
using HoneyGrid.Contracts;
using HoneyGrid.Functions.Ai;
using HoneyGrid.Functions.Classification;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Klasyfikacja zdarzeń w czasie zbliżonym do rzeczywistego (Track B).
///
/// Wyzwalacz Change Feed na kontenerze <c>events</c>. Dla każdego zdarzenia bez
/// pola <c>classification</c>:
///   1) jeśli klasyfikator AI jest aktywny (<see cref="OpenAiClassifier"/>),
///      klasyfikuje cały wsad jednym wywołaniem Azure OpenAI,
///   2) dla pozycji, których model nie zwrócił / nie dało się sparsować, używa
///      <see cref="StubClassifier"/> (deterministyczny fallback),
///   3) zapisuje wynik operacją PATCH (tani, częściowy zapis).
///
/// Idempotencja / brak pętli: PATCH ponownie wyzwala Change Feed, ale przy drugim
/// przejściu pole <c>classification</c> już istnieje, więc zdarzenie jest pomijane.
/// Osobny prefiks dzierżaw ("classify") — nie koliduje z FanOutToSignalR.
/// </summary>
public sealed class ClassifyEvents
{
    private readonly CosmosClient _cosmos;
    private readonly OpenAiClassifier _classifier;
    private readonly ILogger<ClassifyEvents> _logger;
    private readonly string _databaseName;

    public ClassifyEvents(
        CosmosClient cosmos,
        OpenAiClassifier classifier,
        IConfiguration config,
        ILogger<ClassifyEvents> logger)
    {
        _cosmos = cosmos;
        _classifier = classifier;
        _logger = logger;
        _databaseName = config["CosmosDatabase"] ?? "honeygrid";
    }

    [Function(nameof(ClassifyEvents))]
    [SignalROutput(HubName = "attacks")]
    public async Task<SignalRMessageAction?> Run(
        [CosmosDBTrigger(
            databaseName: "%CosmosDatabase%",
            containerName: "events",
            Connection = "CosmosConnection",
            LeaseContainerName = "leases",
            // Własny prefiks dzierżaw — KRYTYCZNE: każdy wyzwalacz Change Feed na
            // tym samym kontenerze MUSI mieć osobny prefiks/kontener dzierżaw,
            // inaczej procesory odbierałyby sobie dzierżawy partycji.
            LeaseContainerPrefix = "classify",
            CreateLeaseContainerIfNotExists = true)]
        IReadOnlyList<HoneypotEvent> changes,
        CancellationToken cancellationToken)
    {
        if (changes is null || changes.Count == 0) return null;

        // Klasyfikujemy tylko zdarzenia jeszcze bez wyniku (pomija własne PATCH-e).
        var pending = changes.Where(e => e.Classification is null).ToList();
        if (pending.Count == 0) return null;

        var container = _cosmos.GetContainer(_databaseName, "events");

        IReadOnlyList<ClassificationInfo?> aiResults = new ClassificationInfo?[pending.Count];
        SignalRMessageAction? auditAction = null;

        if (_classifier.Enabled)
        {
            var (results, latency, isSuccess) = await _classifier.ClassifyAsync(pending, cancellationToken);
            aiResults = results;

            var auditEntry = new AiAuditEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Server = $"HoneyGrid Classifier ({_classifier.DeploymentName})",
                Tool = "classify_events_batch",
                Input = JsonSerializer.Serialize(pending.Select(e => new { ip = e.AttackerIp, type = e.EventType })),
                LatencyMs = (int)latency,
                Status = isSuccess ? "success" : "error"
            };

            auditAction = new SignalRMessageAction("aiAuditLog", [new[] { auditEntry }]);
        }

        var classified = 0;
        var usedAi = 0;

        for (var i = 0; i < pending.Count; i++)
        {
            var evt = pending[i];
            var fromAi = aiResults[i];
            var useAi = IsUsable(fromAi);
            var classification = useAi ? fromAi! : StubClassifier.Classify(evt);

            try
            {
                await container.PatchItemAsync<HoneypotEvent>(
                    id: evt.Id.ToString(),
                    partitionKey: new PartitionKey(evt.AttackerIp),
                    patchOperations: [PatchOperation.Add("/classification", classification)],
                    cancellationToken: cancellationToken);
                classified++;
                if (useAi) usedAi++;
            }
            catch (CosmosException ex)
            {
                _logger.LogWarning(ex,
                    "Klasyfikacja: PATCH zdarzenia {EventId} nieudany (status {Status}).",
                    evt.Id, ex.StatusCode);
            }
        }

        if (classified > 0)
        {
            _logger.LogInformation(
                "Sklasyfikowano {Count} zdarzeń ({Ai} z AI, {Stub} ze stuba).",
                classified, usedAi, classified - usedAi);
        }

        return auditAction;
    }

    /// <summary>Wynik modelu jest użyteczny, gdy ma fazę lub kategorię.</summary>
    private static bool IsUsable(ClassificationInfo? c) =>
        c is not null && (c.KillChainPhase is not null || !string.IsNullOrWhiteSpace(c.Category));
}
