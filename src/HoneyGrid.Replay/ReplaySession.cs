using System.Text.Json.Serialization;

namespace HoneyGrid.Replay;

/// <summary>
/// Kompletne nagranie sesji TTY gotowe do odtworzenia po stronie frontendu (xterm.js).
/// Kontrakt zgodny 1:1 z frontendowym typem SessionReplay
/// (camelCase: sessionId, attackerIp, sensorId, startedAt, durationMs, frames[]).
/// </summary>
/// <param name="SessionId">Identyfikator sesji (klucz partycji Cosmos /sessionId).</param>
/// <param name="AttackerIp">Adres IP atakującego.</param>
/// <param name="SensorId">Identyfikator sensora, który zarejestrował sesję.</param>
/// <param name="StartedAt">Czas rozpoczęcia sesji (UTC).</param>
/// <param name="DurationMs">Czas trwania nagrania (ms) = OffsetMs ostatniej klatki.</param>
/// <param name="Frames">Klatki nagrania w kolejności chronologicznej.</param>
public sealed record ReplaySession(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("attackerIp")] string AttackerIp,
    [property: JsonPropertyName("sensorId")] string SensorId,
    [property: JsonPropertyName("startedAt")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("frames")] IReadOnlyList<ReplayFrame> Frames);
