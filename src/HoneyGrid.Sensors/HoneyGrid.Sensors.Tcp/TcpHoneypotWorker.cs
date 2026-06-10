using System.Net;
using System.Net.Sockets;
using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.Tcp;

/// <summary>
/// Generyczny nasłuch TCP — szkielet honeypota niskointeraktywnego.
/// Każde przychodzące połączenie jest rejestrowane jako zdarzenie "connect".
/// </summary>
public sealed class TcpHoneypotWorker(ILogger<TcpHoneypotWorker> logger) : BackgroundService
{
    // TODO (Track A, Tydzień 2): porty z konfiguracji (IOptions<TcpSensorOptions>).
    private const int ListenPort = 2323;
    private const string SensorId = "tcp-local-01";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, ListenPort);
        listener.Start();
        logger.LogInformation("Honeypot TCP nasłuchuje na porcie {Port}", ListenPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stoppingToken);
                var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "unknown";

                // Szkielet zdarzenia — docelowo publikowane do Event Hub.
                var evt = new HoneypotEvent
                {
                    Id = Guid.NewGuid(),
                    AttackerIp = remote,
                    SensorId = SensorId,
                    SensorType = SensorType.Rdp,
                    Timestamp = DateTimeOffset.UtcNow,
                    EventType = EventType.Connect,
                };

                // TODO (Track A, Tydzień 3): wysłać evt do Event Hub (EventHubProducerClient).
                // TODO (Track A, Tydzień 3): banner-grabbing — odczytać pierwsze bajty od klienta
                //                            i zapisać surowy payload do Blob Storage (rawRef).
                logger.LogInformation("Połączenie z {AttackerIp}: {Event}", remote, HoneyGridJson.Serialize(evt));
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
