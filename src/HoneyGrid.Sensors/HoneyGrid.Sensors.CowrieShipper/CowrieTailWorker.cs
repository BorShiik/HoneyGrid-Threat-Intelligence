using HoneyGrid.Sensors.Common;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.CowrieShipper;

/// <summary>
/// Serwis w tle śledzący plik logu JSON Cowrie (follow-file) i publikujący zmapowane
/// zdarzenia przez <see cref="IEventSink"/>.
///
/// Semantyka follow-file:
///  - czyta nowe linie w miarę dopisywania ich do pliku,
///  - obsługuje rotację: gdy plik zostanie skrócony (rozmiar mniejszy niż dotychczas
///    odczytana pozycja) lub zniknie, zaczyna odczyt od początku nowego pliku,
///  - pomija linie częściowe (bez znaku nowej linii) do czasu ich domknięcia.
///
/// Tryb ReadToEndAndStop=true: wczytuje cały plik raz i kończy — wygodny do lokalnego
/// przetworzenia pliku-próbki (fixtures/cowrie/cowrie-sample.json).
/// </summary>
public sealed class CowrieTailWorker(
    IEventSink sink,
    TtyBlobUploader ttyUploader,
    IOptions<CowrieShipperOptions> options,
    ILogger<CowrieTailWorker> logger) : BackgroundService
{
    private readonly CowrieShipperOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "CowrieShipper śledzi plik {Path} (ReadToEndAndStop={Once})",
            _options.LogPath, _options.ReadToEndAndStop);

        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(100, _options.PollIntervalMs));
        long position = 0;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!File.Exists(_options.LogPath))
                {
                    if (_options.ReadToEndAndStop)
                    {
                        logger.LogWarning("Plik logu {Path} nie istnieje — kończę (tryb jednorazowy).", _options.LogPath);
                        return;
                    }

                    await Task.Delay(pollDelay, stoppingToken);
                    continue;
                }

                position = await ReadNewLinesAsync(position, stoppingToken);

                if (_options.ReadToEndAndStop)
                {
                    logger.LogInformation("CowrieShipper: zakończono jednorazowy odczyt pliku.");
                    return;
                }

                await Task.Delay(pollDelay, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normalne zamknięcie aplikacji.
        }
    }

    /// <summary>
    /// Odczytuje nowe linie od pozycji <paramref name="position"/>. Zwraca nową pozycję.
    /// Wykrywa rotację (plik krótszy niż pozycja) i czyta wtedy od początku.
    /// </summary>
    private async Task<long> ReadNewLinesAsync(long position, CancellationToken stoppingToken)
    {
        var length = new FileInfo(_options.LogPath).Length;

        // Rotacja/obcięcie: plik krótszy niż ostatnia pozycja → start od zera.
        if (length < position)
        {
            logger.LogInformation("Wykryto rotację logu {Path} — czytam od początku.", _options.LogPath);
            position = 0;
        }

        if (length == position)
        {
            return position; // brak nowych danych
        }

        await using var stream = new FileStream(
            _options.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(position, SeekOrigin.Begin);

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync(stoppingToken)) is not null)
        {
            await ProcessLineAsync(line, stoppingToken);
        }

        // Pozycja = ile bajtów faktycznie skonsumował reader.
        return stream.Position;
    }

    private async Task ProcessLineAsync(string line, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var evt = CowrieEventMapper.Map(line, out var skipReason);
        if (evt is null)
        {
            logger.LogDebug("Pominięto linię Cowrie ({Reason})", skipReason);
            return;
        }

        // Domknięcie sesji z referencją TTY → skopiuj binarny plik nagrania do Blob.
        // Pole "ttylog" w linii to lokalna ścieżka pliku na współdzielonym wolumenie.
        if (evt.TtyRef is not null && evt.SessionId is not null)
        {
            var ttylogPath = ExtractTtylogPath(line);
            if (!string.IsNullOrEmpty(ttylogPath))
            {
                await ttyUploader.UploadAsync(evt.SessionId, ttylogPath, stoppingToken);
            }
        }

        await sink.EnqueueAsync(evt, stoppingToken);
    }

    /// <summary>Wyłuskuje lokalną ścieżkę pliku TTY z pola "ttylog" linii JSON Cowrie.</summary>
    private static string? ExtractTtylogPath(string jsonLine)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
            return doc.RootElement.TryGetProperty("ttylog", out var el) &&
                   el.ValueKind == System.Text.Json.JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
