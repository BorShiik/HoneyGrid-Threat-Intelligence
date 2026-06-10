using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoneyGrid.Contracts;

/// <summary>
/// Wspólna konfiguracja System.Text.Json dla całej platformy HoneyGrid.
/// Każdy komponent (sensory, ingestia, API, funkcje) MUSI używać tych opcji,
/// aby zachować spójny format zdarzeń w Event Hub / Cosmos DB / SignalR.
/// </summary>
public static class HoneyGridJson
{
    /// <summary>
    /// camelCase, pomijanie pól null, enumy jako stringi.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    /// <summary>Serializuje zdarzenie do JSON zgodnie z kontraktem HoneyGrid.</summary>
    public static string Serialize(HoneypotEvent evt) =>
        JsonSerializer.Serialize(evt, Options);

    /// <summary>Deserializuje zdarzenie z JSON zgodnie z kontraktem HoneyGrid.</summary>
    public static HoneypotEvent? Deserialize(string json) =>
        JsonSerializer.Deserialize<HoneypotEvent>(json, Options);
}
