using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Common;
using Microsoft.Extensions.Options;

namespace HoneyGrid.Sensors.Tests;

/// <summary>
/// Testy kanału/sinka w trybie lokalnym — enqueue N zdarzeń bez rzucania wyjątków,
/// odczyt zdarzeń przez stronę czytającą kanału.
/// </summary>
public sealed class ChannelEventSinkTests
{
    private static HoneypotEvent SampleEvent(int i) => new()
    {
        Id = Guid.NewGuid(),
        AttackerIp = $"10.0.0.{i % 256}",
        SensorId = "test-sensor",
        SensorType = SensorType.Web,
        Timestamp = DateTimeOffset.UtcNow,
        EventType = EventType.HttpRequest,
    };

    private static HoneypotEventChannel NewChannel(int capacity = 10_000)
    {
        var options = Options.Create(new SensorOptions { ChannelCapacity = capacity, LocalLogOnly = true });
        return new HoneypotEventChannel(options);
    }

    [Fact]
    public async Task Enqueue_wielu_zdarzen_nie_rzuca_i_sa_obserwowalne()
    {
        var channel = NewChannel();
        var sink = new ChannelEventSink(channel);

        const int n = 500;
        for (var i = 0; i < n; i++)
        {
            await sink.EnqueueAsync(SampleEvent(i));
        }

        channel.Writer.Complete();

        var count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
        {
            count++;
        }

        Assert.Equal(n, count);
    }

    [Fact]
    public async Task DropOldest_przy_przepelnieniu_nie_blokuje_zapisu()
    {
        // Mały bufor + brak czytelnika → polityka DropOldest gwarantuje, że zapis nigdy nie blokuje.
        var channel = NewChannel(capacity: 8);
        var sink = new ChannelEventSink(channel);

        // Zapisujemy znacznie więcej niż pojemność — nie powinno się zawiesić ani rzucić.
        for (var i = 0; i < 1_000; i++)
        {
            await sink.EnqueueAsync(SampleEvent(i));
        }

        channel.Writer.Complete();

        var count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
        {
            count++;
        }

        // Po DropOldest w buforze zostaje co najwyżej tyle, ile wynosi pojemność.
        Assert.True(count <= 8, $"Oczekiwano <= 8, było {count}");
        Assert.True(count > 0);
    }

    [Fact]
    public async Task EventHubShipper_w_trybie_lokalnym_konsumuje_zdarzenia()
    {
        // Shipper w LocalLogOnly powinien wystartować, opróżnić kanał i zatrzymać się bez błędu.
        var options = Options.Create(new SensorOptions { LocalLogOnly = true, ChannelCapacity = 100 });
        var channel = new HoneypotEventChannel(options);
        var sink = new ChannelEventSink(channel);

        for (var i = 0; i < 10; i++)
        {
            await sink.EnqueueAsync(SampleEvent(i));
        }

        var shipper = new EventHubShipper(
            channel,
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EventHubShipper>.Instance);

        using var cts = new CancellationTokenSource();
        await shipper.StartAsync(cts.Token);

        // Daj chwilę na konsumpcję, potem zatrzymaj.
        await Task.Delay(200, cts.Token);
        await shipper.StopAsync(CancellationToken.None);

        // Brak wyjątku = sukces; kanał nie powinien już zawierać zaległości po konsumpcji.
        Assert.False(channel.Reader.TryRead(out _));
    }
}
