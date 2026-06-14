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
        app.MapGet("/api/sessions/{id}/replay", GetReplayAsync)
            .WithName("GetSessionReplay")
            .WithTags("Sessions");

        return app;
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
}
