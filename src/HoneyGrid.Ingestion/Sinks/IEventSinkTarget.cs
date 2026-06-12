using HoneyGrid.Contracts;

namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Cel fan-outu wzbogaconych zdarzeń. Worker wywołuje wszystkie zarejestrowane
/// sinki dla każdego zdarzenia; błąd jednego sinka NIE blokuje pozostałych
/// (worker łapie wyjątki per sink i loguje).
/// </summary>
public interface IEventSinkTarget
{
    /// <summary>Nazwa sinka do logowania diagnostycznego.</summary>
    string Name { get; }

    /// <summary>Zapisuje / przekazuje wzbogacone zdarzenie do celu.</summary>
    ValueTask WriteAsync(HoneypotEvent evt, CancellationToken ct);
}
