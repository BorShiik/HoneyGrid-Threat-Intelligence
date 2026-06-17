using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.CowrieShipper;

/// <summary>
/// Kopiuje binarne pliki nagrań TTY Cowrie (z lokalnego, współdzielonego wolumenu)
/// do kontenera Blob Storage. Uwierzytelnianie bezkluczowe: DefaultAzureCredential
/// (tożsamość id-sensor z rolą Storage Blob Data Contributor).
///
/// Odporność: gdy <see cref="CowrieShipperOptions.BlobServiceUri"/> jest puste,
/// upload jest pomijany (tryb lokalny/dev). Każdy błąd uploadu jest logowany,
/// ale NIGDY nie przerywa pętli śledzenia logu (zwraca po prostu false).
/// Klient Blob tworzony jest leniwie przy pierwszym faktycznym uploadzie.
/// </summary>
public sealed class TtyBlobUploader(
    IOptions<CowrieShipperOptions> options,
    ILogger<TtyBlobUploader> logger)
{
    private readonly CowrieShipperOptions _options = options.Value;
    private readonly Lazy<BlobServiceClient?> _client = new(() =>
        string.IsNullOrWhiteSpace(options.Value.BlobServiceUri)
            ? null
            : new BlobServiceClient(new Uri(options.Value.BlobServiceUri), new DefaultAzureCredential()));

    /// <summary>True, gdy skonfigurowano docelowy magazyn Blob (BlobServiceUri niepuste).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.BlobServiceUri);

    /// <summary>
    /// Wysyła plik <paramref name="localTtyPath"/> jako blob "<sessionId>.tty"
    /// do kontenera <see cref="CowrieShipperOptions.TtyContainer"/>. Zwraca true przy sukcesie,
    /// false gdy pominięto (brak konfiguracji / brak pliku) lub gdy wystąpił błąd.
    /// </summary>
    public async Task<bool> UploadAsync(string sessionId, string localTtyPath, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            logger.LogDebug(
                "Upload TTY pominięty (BlobServiceUri puste) — ttyRef ustawione logicznie dla sesji {Session}.",
                sessionId);
            return false;
        }

        // Sidecar widzi współdzielony wolumen pod innym punktem montowania niż Cowrie,
        // więc ścieżkę z cowrie.json mapujemy: nazwa pliku + lokalny katalog TTY shippera.
        var resolvedPath = Path.Combine(_options.TtyLocalDir, Path.GetFileName(localTtyPath));

        if (!File.Exists(resolvedPath))
        {
            logger.LogWarning(
                "Plik TTY {Path} nie istnieje — pomijam upload sesji {Session}.",
                resolvedPath, sessionId);
            return false;
        }

        try
        {
            var service = _client.Value!;
            var container = service.GetBlobContainerClient(_options.TtyContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);
            var blobName = TtyBlobNaming.BlobName(sessionId);
            var blob = container.GetBlobClient(blobName);

            await using var fs = File.OpenRead(resolvedPath);
            await blob.UploadAsync(fs, overwrite: true, ct);

            logger.LogInformation(
                "Wysłano nagranie TTY sesji {Session} → {Container}/{Blob} ({Bytes} B).",
                sessionId, _options.TtyContainer, blobName, fs.Length);
            return true;
        }
        catch (Exception ex)
        {
            // Świadomie łapiemy wszystko: upload TTY nie może nigdy wywrócić pętli tail.
            logger.LogError(ex,
                "Błąd uploadu nagrania TTY sesji {Session} z {Path} — kontynuuję.",
                sessionId, localTtyPath);
            return false;
        }
    }
}
