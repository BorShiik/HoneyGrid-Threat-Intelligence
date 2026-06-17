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

        // Tworzymy katalog nagrań TTY na WSPÓŁDZIELONYM wolumenie. Cowrie (drugi
        // kontener) nie zakłada go sam na świeżym wolumenie i bez niego pada
        // FileNotFoundError przy zapisie ttylog. Shipper montuje ten sam wolumen
        // (RW), więc katalog utworzony tutaj jest widoczny dla Cowrie po jego
        // stronie montażu — zanim atakujący rozpocznie sesję.
        try
        {
            Directory.CreateDirectory(_options.TtyLocalDir);
            // KLUCZOWE: katalog tworzy shipper (inny uid niż Cowrie). Bez praw zapisu
            // dla wszystkich Cowrie dostaje PermissionError [Errno 13] przy zapisie
            // ttylog. Ustawiamy 0777, by proces Cowrie (user 'cowrie') mógł pisać.
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_options.TtyLocalDir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }
            logger.LogInformation("Katalog nagrań TTY gotowy (0777): {Dir}", _options.TtyLocalDir);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Nie udało się utworzyć/uprawnić katalogu TTY {Dir}", _options.TtyLocalDir);
        }

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
