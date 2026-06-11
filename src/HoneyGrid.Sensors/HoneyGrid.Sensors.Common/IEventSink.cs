using HoneyGrid.Contracts;

namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Punkt wejścia dla sensorów — wszystkie sensory wołają wyłącznie tę abstrakcję,
/// nie wiedząc nic o Event Hub. Implementacja zapisuje zdarzenie do bufora (Channel),
/// a osobny serwis w tle (<see cref="EventHubShipper"/>) wysyła je paczkami do Azure.
/// </summary>
public interface IEventSink
{
    /// <summary>
    /// Umieszcza zdarzenie w buforze do asynchronicznej wysyłki.
    /// Zwraca natychmiast (lub po krótkim oczekiwaniu na miejsce w buforze) —
    /// nie blokuje wątku obsługującego atakującego.
    /// </summary>
    ValueTask EnqueueAsync(HoneypotEvent evt, CancellationToken cancellationToken = default);
}
