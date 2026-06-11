using System.Threading.Channels;
using HoneyGrid.Contracts;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Współdzielony, ograniczony (bounded) kanał zdarzeń łączący producentów (sensory)
/// z konsumentem (<see cref="EventHubShipper"/>).
///
/// Polityka backpressure (przeciwciśnienia): wybrano <see cref="BoundedChannelFullMode.DropOldest"/>.
/// Uzasadnienie: honeypot pod masowym skanowaniem generuje lawinę zdarzeń; w razie zapchania
/// bufora wolimy ODRZUCIĆ najstarsze (już częściowo nieaktualne) zdarzenia niż blokować wątek
/// obsługujący atakującego (co zdradziłoby, że to pułapka) albo wywrócić proces brakiem pamięci.
/// Utrata pojedynczych zdarzeń telemetrycznych jest akceptowalna — liczy się ciągłość działania.
/// </summary>
public sealed class HoneypotEventChannel
{
    private readonly Channel<HoneypotEvent> _channel;

    public HoneypotEventChannel(IOptions<SensorOptions> options)
    {
        var capacity = Math.Max(1, options.Value.ChannelCapacity);
        _channel = Channel.CreateBounded<HoneypotEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Strona zapisu — używana przez <see cref="ChannelEventSink"/>.</summary>
    public ChannelWriter<HoneypotEvent> Writer => _channel.Writer;

    /// <summary>Strona odczytu — używana przez <see cref="EventHubShipper"/>.</summary>
    public ChannelReader<HoneypotEvent> Reader => _channel.Reader;
}
