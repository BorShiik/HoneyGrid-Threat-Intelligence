namespace HoneyGrid.Ingestion;

/// <summary>
/// Konfiguracja workera ingestii (sekcja "Ingestion" w appsettings / zmienne środowiskowe
/// "Ingestion__&lt;Nazwa&gt;" ustawiane przez Bicep).
///
/// UWAGA: nazwy właściwości są KONTRAKTEM z infrastrukturą (infra/ — Bicep) —
/// każda zmiana nazwy musi być zsynchronizowana ze zmiennymi środowiskowymi
/// kontenera, inaczej konfiguracja po cichu nie zostanie zbindowana (bug z tygodnia 2!).
///
/// Architektura bezkluczowa: żadnych connection stringów — wyłącznie w pełni
/// kwalifikowane przestrzenie nazw / endpointy + DefaultAzureCredential.
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>Nazwa sekcji konfiguracji.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>FQDN przestrzeni nazw Event Hubs, np. "hg-dev-ehns-x.servicebus.windows.net".</summary>
    public string? EventHubFullyQualifiedNamespace { get; set; }

    /// <summary>Nazwa Event Hub (encji) z surowymi zdarzeniami honeypotów.</summary>
    public string EventHubName { get; set; } = "honeypot-events";

    /// <summary>Grupa konsumencka Event Hub.</summary>
    public string ConsumerGroup { get; set; } = "$Default";

    /// <summary>Endpoint usługi Blob, np. "https://hgdevstx.blob.core.windows.net".</summary>
    public string? BlobServiceUri { get; set; }

    /// <summary>Kontener Blob na checkpointy EventProcessorClient.</summary>
    public string CheckpointContainer { get; set; } = "checkpoints";

    /// <summary>Kontener Blob na surowe zdarzenia (audyt).</summary>
    public string RawContainer { get; set; } = "raw";

    /// <summary>Endpoint Cosmos DB, np. "https://....documents.azure.com:443/".</summary>
    public string? CosmosEndpoint { get; set; }

    /// <summary>Nazwa bazy Cosmos DB.</summary>
    public string CosmosDatabase { get; set; } = "honeygrid";

    /// <summary>Kontener Cosmos DB na wzbogacone zdarzenia (klucz partycji: /attackerIp).</summary>
    public string CosmosEventsContainer { get; set; } = "events";

    /// <summary>Kontener Cosmos DB na projekcję sesji (klucz partycji: /sessionId) — Session Replay.</summary>
    public string CosmosSessionsContainer { get; set; } = "sessions";

    /// <summary>FQDN przestrzeni nazw Service Bus, np. "hg-dev-sbns-x.servicebus.windows.net".</summary>
    public string? ServiceBusFullyQualifiedNamespace { get; set; }

    /// <summary>Kolejka Service Bus zasilająca klasyfikator AI (Track B).</summary>
    public string ServiceBusQueue { get; set; } = "ai-classify";

    /// <summary>Ścieżka do bazy GeoLite2-City (.mmdb); względna — względem katalogu binarki.</summary>
    public string GeoIpCityDbPath { get; set; } = "geoip/GeoLite2-City.mmdb";

    /// <summary>Ścieżka do bazy GeoLite2-ASN (.mmdb); względna — względem katalogu binarki.</summary>
    public string GeoIpAsnDbPath { get; set; } = "geoip/GeoLite2-ASN.mmdb";

    /// <summary>Klucz API AbuseIPDB; pusty = wzbogacanie TI wyłączone.</summary>
    public string AbuseIpDbApiKey { get; set; } = "";

    /// <summary>Czy wykonywać odwrotne zapytania DNS (PTR) dla IP atakujących.</summary>
    public bool EnableReverseDns { get; set; } = true;

    /// <summary>Limit czasu (ms) pojedynczego zapytania rDNS.</summary>
    public int RdnsTimeoutMs { get; set; } = 1500;

    /// <summary>
    /// Endpoint Logs Ingestion (DCE) dla Microsoft Sentinel,
    /// np. "https://hg-dev-dce-xxxx.swedencentral-1.ingest.monitor.azure.com".
    /// Pusty = sink Sentinel wyłączony (to opcjonalna, druga ścieżka telemetrii).
    /// </summary>
    public string DceLogsIngestionEndpoint { get; set; } = "";

    /// <summary>ImmutableId reguły DCR (np. "dcr-..."). Pusty = sink Sentinel wyłączony.</summary>
    public string DcrImmutableId { get; set; } = "";

    /// <summary>Nazwa strumienia z deklaracji DCR (kontrakt kolumn tabeli Cowrie_CL).</summary>
    public string DcrStreamName { get; set; } = "Custom-CowrieStream";

    /// <summary>Maksymalna liczba wierszy w jednej paczce Logs Ingestion (Sentinel).</summary>
    public int SentinelBatchSize { get; set; } = 100;

    /// <summary>Maksymalny czas (ms) buforowania paczki Sentinel przed wysyłką.</summary>
    public int SentinelFlushIntervalMs { get; set; } = 5000;

    /// <summary>Maksymalna liczba zdarzeń w jednej paczce Service Bus.</summary>
    public int ServiceBusBatchSize { get; set; } = 50;

    /// <summary>Maksymalny czas (ms) buforowania paczki Service Bus przed wysyłką.</summary>
    public int ServiceBusFlushIntervalMs { get; set; } = 5000;

    /// <summary>Co ile zdarzeń (per partycja) zapisywać checkpoint w Blob.</summary>
    public int CheckpointEveryEvents { get; set; } = 50;

    /// <summary>
    /// Tryb suchego startu: gdy true, worker NIE łączy się z żadną usługą Azure —
    /// służy wyłącznie do lokalnego uruchomienia hosta (dev).
    /// </summary>
    public bool DryRun { get; set; } = false;
}
