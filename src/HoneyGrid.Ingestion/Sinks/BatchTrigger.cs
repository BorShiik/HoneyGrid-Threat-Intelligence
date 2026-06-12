namespace HoneyGrid.Ingestion.Sinks;

/// <summary>
/// Czysta (testowalna) logika decyzji o flushu paczki Service Bus:
/// wysyłamy gdy (a) bufor osiągnął maksymalny rozmiar paczki, LUB
/// (b) w buforze cokolwiek jest i upłynął interwał flush.
/// Pusty bufor nigdy nie wyzwala wysyłki.
/// </summary>
public sealed class BatchTrigger(int maxBatchSize, TimeSpan flushInterval)
{
    /// <summary>Maksymalny rozmiar paczki.</summary>
    public int MaxBatchSize { get; } = maxBatchSize;

    /// <summary>Maksymalny czas buforowania.</summary>
    public TimeSpan FlushInterval { get; } = flushInterval;

    /// <summary>Czy należy teraz wysłać zbuforowane zdarzenia.</summary>
    public bool ShouldFlush(int bufferedCount, TimeSpan elapsedSinceLastFlush)
    {
        if (bufferedCount <= 0)
        {
            return false;
        }

        return bufferedCount >= MaxBatchSize || elapsedSinceLastFlush >= FlushInterval;
    }
}
