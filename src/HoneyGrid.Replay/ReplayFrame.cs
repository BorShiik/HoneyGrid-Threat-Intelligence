using System.Text.Json.Serialization;

namespace HoneyGrid.Replay;

/// <summary>
/// Pojedyncza klatka nagrania TTY — jeden rekord z logu Cowrie "ttylog".
/// Kontrakt zgodny 1:1 z frontendem (camelCase: offsetMs, type, data).
/// </summary>
/// <param name="OffsetMs">
/// Przesunięcie czasowe (ms) względem pierwszego rekordu w nagraniu.
/// Pierwsza klatka ma zawsze OffsetMs = 0. Wartości są monotonicznie niemalejące.
/// </param>
/// <param name="Type">
/// Kierunek danych: 'o' = output honeypota (OP_WRITE, op 1),
/// 'i' = input atakującego (OP_READ, op 2).
/// </param>
/// <param name="Data">Zdekodowana (UTF-8, lossy) zawartość rekordu.</param>
public sealed record ReplayFrame(
    [property: JsonPropertyName("offsetMs")] long OffsetMs,
    [property: JsonPropertyName("type")] char Type,
    [property: JsonPropertyName("data")] string Data);
