using Microsoft.Extensions.Options;

namespace HoneyGrid.Ingestion;

/// <summary>
/// Walidacja konfiguracji ingestii wykonywana przy starcie hosta (ValidateOnStart).
/// Cel: brakująca / literówkowa zmienna środowiskowa z Bicep ma ubić proces
/// z czytelnym komunikatem, zamiast po cichu działać z pustą konfiguracją.
/// W trybie DryRun nic nie wymagamy — host ma się po prostu uruchomić.
/// </summary>
public sealed class IngestionOptionsValidator : IValidateOptions<IngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestionOptions options)
    {
        if (options.DryRun)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.EventHubFullyQualifiedNamespace))
        {
            errors.Add("Brak Ingestion:EventHubFullyQualifiedNamespace (env: Ingestion__EventHubFullyQualifiedNamespace).");
        }

        if (string.IsNullOrWhiteSpace(options.BlobServiceUri))
        {
            errors.Add("Brak Ingestion:BlobServiceUri (env: Ingestion__BlobServiceUri).");
        }

        if (string.IsNullOrWhiteSpace(options.CosmosEndpoint))
        {
            errors.Add("Brak Ingestion:CosmosEndpoint (env: Ingestion__CosmosEndpoint).");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceBusFullyQualifiedNamespace))
        {
            errors.Add("Brak Ingestion:ServiceBusFullyQualifiedNamespace (env: Ingestion__ServiceBusFullyQualifiedNamespace).");
        }

        if (options.CheckpointEveryEvents <= 0)
        {
            errors.Add("Ingestion:CheckpointEveryEvents musi być > 0.");
        }

        if (options.ServiceBusBatchSize <= 0)
        {
            errors.Add("Ingestion:ServiceBusBatchSize musi być > 0.");
        }

        if (options.ServiceBusFlushIntervalMs <= 0)
        {
            errors.Add("Ingestion:ServiceBusFlushIntervalMs musi być > 0.");
        }

        if (options.RdnsTimeoutMs <= 0)
        {
            errors.Add("Ingestion:RdnsTimeoutMs musi być > 0.");
        }

        // Sink Sentinel jest opcjonalny: oba pola puste = sink świadomie wyłączony.
        // Ustawienie tylko JEDNEGO z pary DCE/DCR to niemal na pewno literówka
        // w Bicep — wtedy wolimy ubić proces niż po cichu nie wysyłać do Sentinela.
        var hasDce = !string.IsNullOrWhiteSpace(options.DceLogsIngestionEndpoint);
        var hasDcr = !string.IsNullOrWhiteSpace(options.DcrImmutableId);

        if (hasDce != hasDcr)
        {
            errors.Add(
                "Niespójna konfiguracja Sentinel: ustaw OBA pola Ingestion:DceLogsIngestionEndpoint " +
                "(env: Ingestion__DceLogsIngestionEndpoint) i Ingestion:DcrImmutableId " +
                "(env: Ingestion__DcrImmutableId) — albo żadne (sink wyłączony).");
        }

        if (options.SentinelBatchSize <= 0)
        {
            errors.Add("Ingestion:SentinelBatchSize musi być > 0.");
        }

        if (options.SentinelFlushIntervalMs <= 0)
        {
            errors.Add("Ingestion:SentinelFlushIntervalMs musi być > 0.");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
