using HoneyGrid.Contracts;
using HoneyGrid.Functions.Classification;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace HoneyGrid.Functions.Functions;

/// <summary>
/// Klasyfikacja zdarzeń w czasie zbliżonym do rzeczywistego (Track B).
///
/// Wyzwalacz Change Feed na kontenerze <c>events</c>. Dla każdego zdarzenia bez
/// pola <c>classification</c> dolicza wynik (na razie <see cref="StubClassifier"/>;
/// w Tygodniu 5 zostanie podmieniony na klasyfikator Azure OpenAI) i zapisuje go
/// operacją PATCH (częściowy zapis — tanio w RU).
///
/// Idempotencja / brak pętli: PATCH ponownie wyzwala Change Feed, ale przy drugim
/// przejściu pole <c>classification</c> już istnieje, więc zdarzenie jest pomijane.
/// </summary>
public sealed class ClassifyEvents
{
    private readonly CosmosClient _cosmos;
    private readonly ILogger<ClassifyEvents> _logger;
    private readonly string _databaseName;

    public ClassifyEvents(CosmosClient cosmos, IConfiguration config, ILogger<ClassifyEvents> logger)
    {
        _cosmos = cosmos;
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

        var container = _cosmos.GetContainer(_databaseName, "events");
        var classified = 0;

        foreach (var evt in changes)
        {
            // Pomijamy zdarzenia już sklasyfikowane (także własne PATCH-e — brak pętli).
            if (evt.Classification is not null) continue;

            var classification = StubClassifier.Classify(evt);
            try
            {
                await container.PatchItemAsync<HoneypotEvent>(
                    id: evt.Id.ToString(),
                    partitionKey: new PartitionKey(evt.AttackerIp),
                    patchOperations: [PatchOperation.Add("/classification", classification)],
                    cancellationToken: cancellationToken);
                classified++;
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
            _logger.LogInformation("Sklasyfikowano {Count} zdarzeń (stub).", classified);
        }
    }
}
