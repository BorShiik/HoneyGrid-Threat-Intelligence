namespace HoneyGrid.Sensors.CowrieShipper;

/// <summary>
/// Konfiguracja mostu Cowrie → Event Hub (sekcja "CowrieShipper").
/// </summary>
public sealed class CowrieShipperOptions
{
    public const string SectionName = "CowrieShipper";

    /// <summary>
    /// Ścieżka do pliku logu JSON Cowrie (np. /cowrie/var/log/cowrie/cowrie.json).
    /// Dla dev/testów można wskazać stały plik z repo (fixtures/cowrie/cowrie-sample.json).
    /// </summary>
    public string LogPath { get; set; } = "/cowrie/var/log/cowrie/cowrie.json";

    /// <summary>
    /// Tryb pojedynczego odczytu: wczytaj cały plik od początku i zakończ (bez śledzenia).
    /// Przydatne do lokalnego przetworzenia pliku-próbki. Domyślnie false (follow-file).
    /// </summary>
    public bool ReadToEndAndStop { get; set; }

    /// <summary>Interwał (ms) odpytywania pliku o nowe linie w trybie follow-file.</summary>
    public int PollIntervalMs { get; set; } = 1_000;
}
