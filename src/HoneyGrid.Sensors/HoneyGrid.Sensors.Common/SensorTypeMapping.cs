using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.Common;

/// <summary>Pomocnicze mapowanie tekstowego typu sensora na enum kontraktu.</summary>
public static class SensorTypeMapping
{
    /// <summary>
    /// Mapuje string ("ssh" | "web" | "rdp", wielkość liter bez znaczenia) na <see cref="SensorType"/>.
    /// Dla telnetu i innych nasłuchów TCP zwracamy "rdp" jako najbliższy semantycznie typ,
    /// chyba że konfiguracja jawnie poda inny. Nieznana wartość → null.
    /// </summary>
    public static SensorType? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        SensorTypes.Ssh => SensorType.Ssh,
        SensorTypes.Web => SensorType.Web,
        SensorTypes.Rdp => SensorType.Rdp,
        "telnet" => SensorType.Rdp, // telnet traktujemy jak generyczny nasłuch TCP/RDP
        _ => null,
    };
}
