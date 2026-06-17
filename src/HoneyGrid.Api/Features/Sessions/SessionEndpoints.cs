using System.Net;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using HoneyGrid.Replay;
using Microsoft.Azure.Cosmos;

namespace HoneyGrid.Api.Features.Sessions;

/// <summary>
/// Endpoint odtwarzania sesji (killer feature ① — Session Replay).
/// Mapuje GET /api/sessions/{id}/replay: odczyt metadanych sesji z Cosmos,
/// pobranie binarnego nagrania TTY z Blob, parsing przez <see cref="TtyParser"/>
/// i zwrot <see cref="ReplaySession"/> jako JSON (camelCase) zgodny z frontendem.
///
/// UWAGA integracyjna: CosmosClient i BlobServiceClient są rejestrowane przez orkiestrator
/// (Program.cs) — tutaj jedynie wstrzykiwane. Ten plik celowo nie jest kompilowany w tym PR.
/// </summary>
public static class SessionEndpoints
{
    private const string SessionsContainer = "sessions";
    private const string TtyContainer = "tty";

    /// <summary>Rejestruje endpoint replay. Wywoływane z Program.cs orkiestratora.</summary>
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions", ListSessionsAsync)
            .WithName("ListSessions")
            .WithTags("Sessions");

        app.MapGet("/api/sessions/{id}/replay", GetReplayAsync)
            .WithName("GetSessionReplay")
            .WithTags("Sessions");

        return app;
    }

    /// <summary>
    /// GET /api/sessions — lista sesji (projekcja z kontenera "sessions" budowana przez
    /// CosmosSessionWriter w ingestii). Zwraca SessionSummary[] (camelCase) zgodny z frontendem,
    /// posortowany malejąco po czasie startu. Pusta lista, gdy brak sesji (nie błąd).
    /// </summary>
    private static async Task<IResult> ListSessionsAsync(
        CosmosClient cosmos,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var databaseName = configuration["HoneyGrid:CosmosDatabase"] ?? "honeygrid";
        var container = cosmos.GetContainer(databaseName, SessionsContainer);

        var items = new List<SessionListItem>();
        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.docType = 'session' OR NOT IS_DEFINED(c.docType)");
            using var iterator = container.GetItemQueryIterator<SessionListItem>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                items.AddRange(page);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Kontener jeszcze nie istnieje / brak danych — zwracamy pustą listę.
            return Results.Json(Array.Empty<SessionSummary>(), CamelCaseJson);
        }

        var summaries = items
            .Where(d => !string.IsNullOrWhiteSpace(d.SessionId))
            // Sesje z nagraniem TTY na górę (i gwarantowanie w limicie 500), potem najnowsze —
            // honeypot zbiera setki samych połączeń botów bez komend/TTY.
            .OrderByDescending(d => d.HasTty ?? !string.IsNullOrWhiteSpace(d.TtyRef))
            .ThenByDescending(d => d.StartedAt ?? DateTimeOffset.UnixEpoch)
            .Take(500)
            .Select(d => new SessionSummary(
                SessionId: d.SessionId!,
                AttackerIp: string.IsNullOrEmpty(d.AttackerIp) ? "unknown" : d.AttackerIp!,
                SensorId: string.IsNullOrEmpty(d.SensorId) ? "unknown" : d.SensorId!,
                StartedAt: d.StartedAt ?? DateTimeOffset.UnixEpoch,
                DurationMs: d.DurationMs ?? 0,
                CommandCount: d.CommandCount ?? 0,
                HasTty: d.HasTty ?? !string.IsNullOrWhiteSpace(d.TtyRef),
                Country: d.Country ?? string.Empty,
                CountryName: d.CountryName ?? string.Empty))
            .ToList();

        return Results.Json(summaries, CamelCaseJson);
    }

    private static async Task<IResult> GetReplayAsync(
        string id,
        CosmosClient cosmos,
        BlobServiceClient blob,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var databaseName = configuration["HoneyGrid:CosmosDatabase"] ?? "honeygrid";
        var container = cosmos.GetContainer(databaseName, SessionsContainer);

        // 1) Odczyt dokumentu sesji (PK /sessionId == id).
        SessionDocument? doc;
        try
        {
            var response = await container.ReadItemAsync<SessionDocument>(
                id, new PartitionKey(id), cancellationToken: ct);
            doc = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { error = $"Sesja '{id}' nie istnieje." });
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.TtyRef))
        {
            return Results.NotFound(new { error = $"Sesja '{id}' nie ma nagrania TTY." });
        }

        // 2) Pobranie binarnego nagrania z Blob. ttyRef = "tty/<sessionId>.tty";
        //    nazwa bloba = część po nazwie kontenera.
        var blobName = StripContainerPrefix(doc.TtyRef, TtyContainer);
        var blobClient = blob.GetBlobContainerClient(TtyContainer).GetBlobClient(blobName);

        byte[] ttyBytes;
        try
        {
            var download = await blobClient.DownloadContentAsync(ct);
            ttyBytes = download.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            return Results.NotFound(new { error = $"Nagranie TTY sesji '{id}' nie istnieje w Blob." });
        }

        // 3) Parsing TTY → klatki, budowa kontraktu ReplaySession.
        var frames = TtyParser.Parse(ttyBytes);
        var session = new ReplaySession(
            SessionId: id,
            AttackerIp: doc.AttackerIp ?? "unknown",
            SensorId: doc.SensorId ?? "unknown",
            StartedAt: doc.StartedAt ?? DateTimeOffset.UnixEpoch,
            DurationMs: frames.Count > 0 ? frames[^1].OffsetMs : 0,
            Frames: frames);

        // camelCase zgodny z frontendem (atrybuty JsonPropertyName na rekordach Replay).
        return Results.Json(session, CamelCaseJson);
    }

    /// <summary>
    /// Usuwa wiodący prefiks "<container>/" z referencji ttyRef, zwracając samą nazwę bloba.
    /// Toleruje ref bez prefiksu (zwraca wejście bez zmian).
    /// </summary>
    private static string StripContainerPrefix(string ttyRef, string container)
    {
        var prefix = container + "/";
        return ttyRef.StartsWith(prefix, StringComparison.Ordinal)
            ? ttyRef[prefix.Length..]
            : ttyRef;
    }

    private static readonly JsonSerializerOptions CamelCaseJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Minimalny rzut dokumentu sesji z Cosmos potrzebny do zbudowania nagrania.
    /// (Pełny schemat sesji posiada inne pola — czytamy tylko istotne dla replay.)
    /// </summary>
    private sealed record SessionDocument
    {
        [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
        public string? SessionId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("attackerIp")]
        public string? AttackerIp { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("sensorId")]
        public string? SensorId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("startedAt")]
        public DateTimeOffset? StartedAt { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("ttyRef")]
        public string? TtyRef { get; init; }
    }

    /// <summary>Podsumowanie sesji zwracane przez GET /api/sessions (camelCase, zgodne z frontendem).</summary>
    private sealed record SessionSummary(
        string SessionId,
        string AttackerIp,
        string SensorId,
        DateTimeOffset StartedAt,
        long DurationMs,
        int CommandCount,
        bool HasTty,
        string Country,
        string CountryName);

    /// <summary>Rzut dokumentu sesji z Cosmos do budowy listy (pola opcjonalne — tolerujemy częściowe sesje).</summary>
    private sealed record SessionListItem
    {
        [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
        public string? SessionId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("attackerIp")]
        public string? AttackerIp { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("sensorId")]
        public string? SensorId { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("startedAt")]
        public DateTimeOffset? StartedAt { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("durationMs")]
        public long? DurationMs { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("commandCount")]
        public int? CommandCount { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("hasTty")]
        public bool? HasTty { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("ttyRef")]
        public string? TtyRef { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("country")]
        public string? Country { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("countryName")]
        public string? CountryName { get; init; }
    }
}
