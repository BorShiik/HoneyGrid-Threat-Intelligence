namespace HoneyGrid.Ingestion.Tests;

/// <summary>
/// Walidacja opcji: brakująca konfiguracja z Bicep (env Ingestion__*) ma ubić
/// proces przy starcie z czytelnym błędem zamiast cichej awarii w produkcji.
/// </summary>
public sealed class IngestionOptionsValidatorTests
{
    private static readonly IngestionOptionsValidator Validator = new();

    /// <summary>Komplet wymaganych endpointów (jak z Bicep).</summary>
    private static IngestionOptions ValidOptions() => new()
    {
        EventHubFullyQualifiedNamespace = "hg-dev-ehns-x.servicebus.windows.net",
        BlobServiceUri = "https://hgdevstx.blob.core.windows.net",
        CosmosEndpoint = "https://hg-dev-cosmos.documents.azure.com:443/",
        ServiceBusFullyQualifiedNamespace = "hg-dev-sbns-x.servicebus.windows.net",
        DryRun = false,
    };

    [Fact]
    public void MissingEventHubNamespace_WithDryRunFalse_FailsFast()
    {
        var options = ValidOptions();
        options.EventHubFullyQualifiedNamespace = null;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("EventHubFullyQualifiedNamespace", result.FailureMessage);
    }

    [Fact]
    public void FullConfiguration_Succeeds()
    {
        var result = Validator.Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void DryRun_RequiresNothing()
    {
        // DryRun=true: host ma wystartować lokalnie bez żadnych endpointów Azure.
        var result = Validator.Validate(null, new IngestionOptions { DryRun = true });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void AllEndpointsMissing_ReportsEveryProblem()
    {
        var result = Validator.Validate(null, new IngestionOptions { DryRun = false });

        Assert.True(result.Failed);
        Assert.Contains("EventHubFullyQualifiedNamespace", result.FailureMessage);
        Assert.Contains("BlobServiceUri", result.FailureMessage);
        Assert.Contains("CosmosEndpoint", result.FailureMessage);
        Assert.Contains("ServiceBusFullyQualifiedNamespace", result.FailureMessage);
    }

    // --- Sentinel (Logs Ingestion API): sink opcjonalny, ale pary DCE/DCR nie wolno rozdzielać ---

    [Fact]
    public void SentinelBothFieldsEmpty_IsValid()
    {
        // Oba pola puste = sink Sentinel świadomie wyłączony — to poprawna konfiguracja.
        var options = ValidOptions();
        options.DceLogsIngestionEndpoint = "";
        options.DcrImmutableId = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SentinelBothFieldsSet_IsValid()
    {
        var options = ValidOptions();
        options.DceLogsIngestionEndpoint = "https://hg-dev-dce-x.swedencentral-1.ingest.monitor.azure.com";
        options.DcrImmutableId = "dcr-0123456789abcdef0123456789abcdef";

        var result = Validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SentinelOnlyDceEndpointSet_FailsNamingBothEnvVars()
    {
        // Literówka w Bicep: jest DCE, brak DCR — fail fast z komunikatem
        // wskazującym OBIE zmienne środowiskowe.
        var options = ValidOptions();
        options.DceLogsIngestionEndpoint = "https://hg-dev-dce-x.swedencentral-1.ingest.monitor.azure.com";
        options.DcrImmutableId = "";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Ingestion__DceLogsIngestionEndpoint", result.FailureMessage);
        Assert.Contains("Ingestion__DcrImmutableId", result.FailureMessage);
    }

    [Fact]
    public void SentinelOnlyDcrImmutableIdSet_FailsNamingBothEnvVars()
    {
        var options = ValidOptions();
        options.DceLogsIngestionEndpoint = "";
        options.DcrImmutableId = "dcr-0123456789abcdef0123456789abcdef";

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Ingestion__DceLogsIngestionEndpoint", result.FailureMessage);
        Assert.Contains("Ingestion__DcrImmutableId", result.FailureMessage);
    }

    [Fact]
    public void NonPositiveSentinelBatchSettings_Fail()
    {
        var options = ValidOptions();
        options.SentinelBatchSize = 0;
        options.SentinelFlushIntervalMs = -5;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("SentinelBatchSize", result.FailureMessage);
        Assert.Contains("SentinelFlushIntervalMs", result.FailureMessage);
    }

    [Fact]
    public void NonPositiveNumericSettings_Fail()
    {
        var options = ValidOptions();
        options.CheckpointEveryEvents = 0;
        options.ServiceBusBatchSize = -1;

        var result = Validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("CheckpointEveryEvents", result.FailureMessage);
        Assert.Contains("ServiceBusBatchSize", result.FailureMessage);
    }
}
