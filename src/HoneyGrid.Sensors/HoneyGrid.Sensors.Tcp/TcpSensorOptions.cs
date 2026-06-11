namespace HoneyGrid.Sensors.Tcp;

/// <summary>
/// Konfiguracja nasłuchu TCP (sekcja "TcpSensor" w appsettings).
/// Pozwala uruchomić wiele nasłuchów (po jednym na port) w jednym procesie.
/// </summary>
public sealed class TcpSensorOptions
{
    public const string SectionName = "TcpSensor";

    /// <summary>Porty nasłuchu. Domyślnie 23 (Telnet) i 3389 (RDP).</summary>
    public int[] Ports { get; set; } = [23, 3389];

    /// <summary>Maksymalna liczba bajtów odczytywanych podczas banner-grabbingu.</summary>
    public int BannerGrabBytes { get; set; } = 512;

    /// <summary>Limit czasu (ms) oczekiwania na pierwsze bajty od klienta (banner-grab).</summary>
    public int BannerGrabTimeoutMs { get; set; } = 2_000;

    /// <summary>
    /// Czy dla portu Telnet (23) wysłać fałszywy baner/prompt logowania,
    /// aby zachęcić klienta do interakcji.
    /// </summary>
    public bool SendTelnetBanner { get; set; } = true;
}
