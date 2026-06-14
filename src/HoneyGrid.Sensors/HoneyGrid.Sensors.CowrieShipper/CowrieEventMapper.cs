using System.Text.Json;
using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.CowrieShipper;

/// <summary>
/// Czyste (bezstanowe) mapowanie linii logu JSON Cowrie na <see cref="HoneypotEvent"/>.
/// Parsowanie wyłącznie przez System.Text.Json (BEZ wyrażeń regularnych), nisko-alokacyjnie
/// dzięki <see cref="JsonDocument"/> i odczytowi pól na żądanie.
///
/// Tabela mapowania eventid → EventType:
///   cowrie.session.connect       → Connect
///   cowrie.login.failed          → LoginFailed   (+ CredentialPair)
///   cowrie.login.success         → LoginSuccess  (+ CredentialPair)
///   cowrie.command.input         → Command       (pole input)
///   cowrie.session.file_download → Command*      (downloadHash = "sha256:" + shasum)
///   cowrie.client.size           → Connect*      (metadane sesji)
///   cowrie.log.closed            → Connect*      (metadane sesji; ttyRef gdy ttylog obecny)
///   inne / nieznane              → null (pomijane)
///
/// (*) Cowrie nie ma natywnego typu dla pobrań/metadanych w naszym kontrakcie — mapujemy je
///     na najbliższy semantycznie typ i przenosimy istotne pola (downloadHash, ttyRef).
/// </summary>
public static class CowrieEventMapper
{
    /// <summary>
    /// Mapuje pojedynczą linię JSON Cowrie na zdarzenie kontraktowe.
    /// Zwraca null dla nieznanych/nieobsługiwanych eventid lub niepoprawnego JSON
    /// (parametr <paramref name="skipReason"/> zawiera wtedy powód do debug-logu).
    /// </summary>
    public static HoneypotEvent? Map(string jsonLine, out string? skipReason)
    {
        skipReason = null;

        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            skipReason = "pusta linia";
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonLine);
        }
        catch (JsonException ex)
        {
            skipReason = $"niepoprawny JSON: {ex.Message}";
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                skipReason = "JSON nie jest obiektem";
                return null;
            }

            var eventId = GetString(root, "eventid");
            if (eventId is null)
            {
                skipReason = "brak pola eventid";
                return null;
            }

            var srcIp = GetString(root, "src_ip") ?? "unknown";
            var session = GetString(root, "session");
            var sensor = GetString(root, "sensor") ?? "cowrie-unknown";
            var timestamp = GetTimestamp(root);

            // Wspólny szkielet zdarzenia — pola specyficzne dopisujemy per eventid.
            HoneypotEvent? Build(EventType type, Action<EventBuilder>? extra = null)
            {
                var b = new EventBuilder
                {
                    Id = Guid.NewGuid(),
                    AttackerIp = srcIp,
                    SensorId = sensor,
                    SensorType = SensorType.Ssh, // Cowrie emuluje SSH/Telnet → sensorType = ssh
                    Timestamp = timestamp,
                    EventType = type,
                    SessionId = session,
                };
                extra?.Invoke(b);
                return b.ToEvent();
            }

            switch (eventId)
            {
                case "cowrie.session.connect":
                    return Build(EventType.Connect);

                case "cowrie.login.failed":
                    return Build(EventType.LoginFailed, b => b.Credentials = ReadCredentials(root));

                case "cowrie.login.success":
                    return Build(EventType.LoginSuccess, b => b.Credentials = ReadCredentials(root));

                case "cowrie.command.input":
                    return Build(EventType.Command, b => b.Command = GetString(root, "input"));

                case "cowrie.session.file_download":
                    return Build(EventType.Command, b =>
                    {
                        var shasum = GetString(root, "shasum");
                        if (!string.IsNullOrEmpty(shasum))
                        {
                            b.DownloadHash = $"sha256:{shasum}";
                        }

                        b.Command = GetString(root, "url") is { } url ? $"download {url}" : "download";
                    });

                case "cowrie.client.size":
                    // Metadane rozmiaru terminala — traktujemy jak zdarzenie sesyjne (connect).
                    return Build(EventType.Connect);

                case "cowrie.log.closed":
                    return Build(EventType.Connect, b =>
                    {
                        var ttylog = GetString(root, "ttylog");
                        if (!string.IsNullOrEmpty(ttylog) && session is not null)
                        {
                            // Logiczna referencja "tty/<sessionId>.tty". Faktyczny upload
                            // binarnego pliku TTY do Blob Storage wykonuje TtyBlobUploader
                            // w workerze (gdy skonfigurowano BlobServiceUri).
                            b.TtyRef = TtyBlobNaming.TtyRef("tty", session);
                        }
                    });

                default:
                    skipReason = $"nieobsługiwany eventid: {eventId}";
                    return null;
            }
        }
    }

    /// <summary>Wariant bez parametru out — wygodny w testach i kodzie, gdy powód nieistotny.</summary>
    public static HoneypotEvent? Map(string jsonLine) => Map(jsonLine, out _);

    private static CredentialPair ReadCredentials(JsonElement root) => new()
    {
        Username = GetString(root, "username"),
        Password = GetString(root, "password"),
    };

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static DateTimeOffset GetTimestamp(JsonElement root)
    {
        if (root.TryGetProperty("timestamp", out var el) &&
            el.ValueKind == JsonValueKind.String &&
            el.TryGetDateTimeOffset(out var ts))
        {
            return ts;
        }

        return DateTimeOffset.UtcNow;
    }

    /// <summary>Mutowalny builder ułatwiający warunkowe wypełnianie pól rekordu.</summary>
    private sealed class EventBuilder
    {
        public required Guid Id { get; init; }
        public required string AttackerIp { get; init; }
        public required string SensorId { get; init; }
        public required SensorType SensorType { get; init; }
        public required DateTimeOffset Timestamp { get; init; }
        public required EventType EventType { get; init; }
        public string? SessionId { get; init; }
        public CredentialPair? Credentials { get; set; }
        public string? Command { get; set; }
        public string? DownloadHash { get; set; }
        public string? TtyRef { get; set; }

        public HoneypotEvent ToEvent() => new()
        {
            Id = Id,
            AttackerIp = AttackerIp,
            SensorId = SensorId,
            SensorType = SensorType,
            Timestamp = Timestamp,
            EventType = EventType,
            SessionId = SessionId,
            Credentials = Credentials,
            Command = Command,
            DownloadHash = DownloadHash,
            TtyRef = TtyRef,
        };
    }
}
