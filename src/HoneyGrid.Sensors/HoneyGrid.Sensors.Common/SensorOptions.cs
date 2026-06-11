namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Konfiguracja wspólna producenta zdarzeń honeypota (sekcja "HoneyGrid" w appsettings).
/// Architektura bezkluczowa (keyless): producent łączy się z Event Hub przy użyciu
/// <c>DefaultAzureCredential</c> — lokalnie poprzez <c>az login</c>, a w chmurze poprzez
/// tożsamość zarządzaną (Managed Identity). Nigdy nie używamy connection stringów ani kluczy.
/// </summary>
public sealed class SensorOptions
{
    /// <summary>Nazwa sekcji konfiguracji.</summary>
    public const string SectionName = "HoneyGrid";

    /// <summary>
    /// W pełni kwalifikowana przestrzeń nazw Event Hubs, np. "honeygrid-evh.servicebus.windows.net".
    /// Wymagana, gdy <see cref="LocalLogOnly"/> = false.
    /// </summary>
    public string? EventHubFullyQualifiedNamespace { get; set; }

    /// <summary>Nazwa Event Hub (encji) wewnątrz przestrzeni nazw.</summary>
    public string EventHubName { get; set; } = "honeypot-events";

    /// <summary>Identyfikator sensora, np. "web-weu-01" — trafia do pola sensorId zdarzenia.</summary>
    public string SensorId { get; set; } = "sensor-local-01";

    /// <summary>
    /// Typ sensora jako string ("ssh" | "web" | "rdp") — mapowany na enum kontraktu.
    /// Trzymany jako string, bo pojedynczy proces TCP może obsługiwać różne typy per port.
    /// </summary>
    public string SensorType { get; set; } = "web";

    /// <summary>
    /// Tryb wyłącznie lokalny: gdy true, producent NIE łączy się z Azure —
    /// każde zdarzenie jest jedynie logowane. Domyślnie true, aby cały stack
    /// uruchamiał się na maszynie dewelopera bez subskrypcji Azure.
    /// </summary>
    public bool LocalLogOnly { get; set; } = true;

    /// <summary>Maksymalna pojemność bufora (Channel) zdarzeń oczekujących na wysyłkę.</summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>Maksymalna liczba zdarzeń w jednej paczce EventDataBatch przed wymuszeniem flush.</summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>Maksymalny czas (ms) zbierania paczki zanim zostanie wysłana mimo niepełnego rozmiaru.</summary>
    public int FlushIntervalMs { get; set; } = 2_000;
}
