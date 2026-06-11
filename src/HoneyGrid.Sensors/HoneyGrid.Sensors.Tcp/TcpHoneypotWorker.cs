using System.Net;
using System.Net.Sockets;
using System.Text;
using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Common;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.Tcp;

/// <summary>
/// Generyczny nasłuch TCP — honeypot niskointeraktywny.
/// Uruchamia po jednym nasłuchu na każdy skonfigurowany port (Task per port), wszystkie
/// honorują wspólny stoppingToken. Każde połączenie:
///  1. jest akceptowane,
///  2. poddawane banner-grabbingowi (odczyt pierwszych bajtów z limitem czasu),
///  3. dla Telnetu (23) opcjonalnie otrzymuje fałszywy prompt logowania,
///  4. publikowane jest zdarzenie "connect" przez <see cref="IEventSink"/>.
/// </summary>
public sealed class TcpHoneypotWorker(
    IEventSink sink,
    IOptions<TcpSensorOptions> tcpOptions,
    IOptions<SensorOptions> sensorOptions,
    ILogger<TcpHoneypotWorker> logger) : BackgroundService
{
    private readonly TcpSensorOptions _tcp = tcpOptions.Value;
    private readonly SensorOptions _sensor = sensorOptions.Value;

    private const int TelnetPort = 23;
    private const int RdpPort = 3389;

    // Fałszywy baner Telnet zachęcający do podania loginu.
    private static readonly byte[] TelnetBanner =
        Encoding.ASCII.GetBytes("\r\nUbuntu 22.04.3 LTS\r\nlogin: ");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var ports = _tcp.Ports is { Length: > 0 } ? _tcp.Ports : [TelnetPort, RdpPort];
        logger.LogInformation("Honeypot TCP startuje na portach: {Ports}", string.Join(", ", ports));

        // Jeden Task nasłuchu na port — wszystkie współbieżnie.
        var listeners = ports.Select(port => ListenOnPortAsync(port, stoppingToken));
        await Task.WhenAll(listeners);
    }

    private async Task ListenOnPortAsync(int port, CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, port);

        try
        {
            listener.Start();
            logger.LogInformation("Nasłuch TCP aktywny na porcie {Port}", port);

            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Obsługa pojedynczego połączenia bez blokowania pętli akceptacji.
                _ = HandleClientAsync(client, port, stoppingToken);
            }
        }
        catch (SocketException ex)
        {
            // Np. brak uprawnień do portu < 1024 na maszynie dewelopera — logujemy i kończymy ten port.
            logger.LogError(ex, "Nie udało się nasłuchiwać na porcie {Port}: {Message}", port, ex.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, int port, CancellationToken stoppingToken)
    {
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

        try
        {
            using (client)
            {
                var stream = client.GetStream();

                // Dla Telnetu wyślij fałszywy prompt, by skłonić klienta do interakcji.
                if (port == TelnetPort && _tcp.SendTelnetBanner)
                {
                    await stream.WriteAsync(TelnetBanner, stoppingToken);
                }

                var banner = await GrabBannerAsync(stream, stoppingToken);

                var evt = BuildConnectEvent(port, remote, banner, DateTimeOffset.UtcNow);
                await sink.EnqueueAsync(evt, stoppingToken);

                logger.LogInformation(
                    "Połączenie TCP z {AttackerIp} na porcie {Port}; baner={BannerLen}B",
                    remote, port, banner?.Length ?? 0);
            }
        }
        catch (OperationCanceledException)
        {
            // Zamykanie aplikacji.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Błąd obsługi połączenia z {AttackerIp} na porcie {Port}", remote, port);
        }
    }

    /// <summary>
    /// Odczytuje pierwsze bajty od klienta (banner-grab) z ograniczonym buforem i krótkim limitem czasu.
    /// Zwraca tekst (ASCII, znaki niedrukowalne zastąpione kropką) lub null, gdy nic nie odczytano.
    /// </summary>
    private async Task<string?> GrabBannerAsync(NetworkStream stream, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(_tcp.BannerGrabTimeoutMs);

        var buffer = new byte[Math.Max(1, _tcp.BannerGrabBytes)];
        try
        {
            var read = await stream.ReadAsync(buffer, timeoutCts.Token);
            if (read <= 0)
            {
                return null;
            }

            return SanitizeBanner(buffer.AsSpan(0, read));
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // Limit czasu banner-grab — klient nic nie przysłał. To normalne.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Sanityzacja surowego baneru: tylko drukowalne ASCII, reszta → '.'.</summary>
    internal static string SanitizeBanner(ReadOnlySpan<byte> raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var b in raw)
        {
            sb.Append(b is >= 0x20 and < 0x7f ? (char)b : '.');
        }

        return sb.ToString();
    }

    /// <summary>Buduje zdarzenie "connect" z przechwyconym banerem w polu command.</summary>
    private HoneypotEvent BuildConnectEvent(int port, string attackerIp, string? banner, DateTimeOffset timestamp)
    {
        // Typ sensora per port: 3389 → rdp; pozostałe → z konfiguracji (domyślnie rdp jako generyczny TCP).
        var sensorType = port == RdpPort
            ? SensorType.Rdp
            : SensorTypeMapping.Parse(_sensor.SensorType) ?? SensorType.Rdp;

        return new HoneypotEvent
        {
            Id = Guid.NewGuid(),
            AttackerIp = attackerIp,
            SensorId = _sensor.SensorId,
            SensorType = sensorType,
            Timestamp = timestamp,
            EventType = EventType.Connect,
            // Surowy baner trafia do pola command jako notatka (np. "port=23 banner=...").
            Command = banner is null ? $"port={port}" : $"port={port} banner={banner}",
        };
    }
}
