using HoneyGrid.Contracts;
using HoneyGrid.Functions.Ai;
using HoneyGrid.Functions.Classification;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
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
    public async Task Run(
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
        if (changes is null || changes.Count == 0) return;

        // Klasyfikujemy tylko zdarzenia jeszcze bez wyniku (pomija własne PATCH-e).
        var pending = changes.Where(e => e.Classification is null).ToList();
        if (pending.Count == 0) return;

        var container = _cosmos.GetContainer(_databaseName, "events");

        var aiResults = _classifier.Enabled
            ? await _classifier.ClassifyAsync(pending, cancellationToken)
            : new ClassificationInfo?[pending.Count];

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
    }

    /// <summary>Wynik modelu jest użyteczny, gdy ma fazę lub kategorię.</summary>
    private static bool IsUsable(ClassificationInfo? c) =>
        c is not null && (c.KillChainPhase is not null || !string.IsNullOrWhiteSpace(c.Category));
}
