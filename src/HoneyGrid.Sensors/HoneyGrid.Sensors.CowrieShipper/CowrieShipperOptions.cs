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

    /// <summary>
    /// URI usługi Blob Storage (np. https://stXXXX.blob.core.windows.net), do której
    /// kopiowane są binarne nagrania TTY. Uwierzytelnianie bezkluczowe (DefaultAzureCredential,
    /// tożsamość id-sensor z rolą Storage Blob Data Contributor).
    /// Pusty ('') → upload pomijany (tryb lokalny/dev), ustawiana jest tylko logiczna ttyRef.
    /// </summary>
    public string BlobServiceUri { get; set; } = string.Empty;

    /// <summary>Nazwa kontenera Blob na nagrania TTY. Domyślnie "tty".</summary>
    public string TtyContainer { get; set; } = "tty";

    /// <summary>
    /// Lokalny katalog z plikami nagrań TTY widziany PRZEZ SHIPPERA (sidecar montuje
    /// współdzielony wolumen pod innym punktem niż Cowrie). Pole "ttylog" w cowrie.json
    /// jest ścieżką w przestrzeni Cowrie, więc shipper bierze samą nazwę pliku i składa
    /// ją z tym katalogiem. Domyślnie "/var/log/cowrie/tty" (mount shippera + /tty).
    /// </summary>
    public string TtyLocalDir { get; set; } = "/var/log/cowrie/tty";
}
