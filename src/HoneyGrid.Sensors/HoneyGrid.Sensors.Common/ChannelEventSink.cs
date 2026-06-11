using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Implementacja <see cref="IEventSink"/> oparta o współdzielony <see cref="HoneypotEventChannel"/>.
/// Przy polityce DropOldest zapis praktycznie nigdy nie blokuje — w razie pełnego bufora
/// najstarsze zdarzenie jest cicho usuwane, a nowe przyjęte.
/// </summary>
public sealed class ChannelEventSink(HoneypotEventChannel channel) : IEventSink
{
    /// <inheritdoc />
    public ValueTask EnqueueAsync(HoneypotEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // TryWrite zwraca true natychmiast przy trybie DropOldest (bufor "zawsze ma miejsce").
        if (channel.Writer.TryWrite(evt))
        {
            return ValueTask.CompletedTask;
        }

        // Ścieżka awaryjna (np. kanał zamknięty podczas zamykania aplikacji).
        return channel.Writer.WriteAsync(evt, cancellationToken);
    }
}
