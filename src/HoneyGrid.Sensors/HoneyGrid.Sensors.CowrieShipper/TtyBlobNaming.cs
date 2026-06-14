namespace HoneyGrid.Sensors.CowrieShipper;

/// <summary>
/// Czyste (bezstanowe) reguły nazewnictwa blobów nagrań TTY. Wydzielone z uploadu,
/// aby logikę dało się testować bez połączenia z Azure.
/// </summary>
public static class TtyBlobNaming
{
    /// <summary>
    /// Nazwa bloba w kontenerze TTY dla danej sesji: "<sessionId>.tty".
    /// </summary>
    public static string BlobName(string sessionId) => $"{sessionId}.tty";

    /// <summary>
    /// Logiczna referencja zapisywana w polu <c>TtyRef</c> zdarzenia:
    /// "<container>/<sessionId>.tty" (np. "tty/abc123.tty"). To samo, niezależnie
    /// od tego, czy faktyczny upload się powiódł — frontend/API rozwiązuje ją względem
    /// kontenera 'tty'.
    /// </summary>
    public static string TtyRef(string container, string sessionId) =>
        $"{container}/{BlobName(sessionId)}";
}
