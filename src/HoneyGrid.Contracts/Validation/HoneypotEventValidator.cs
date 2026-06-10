using FluentValidation;

namespace HoneyGrid.Contracts.Validation;

/// <summary>
/// Walidator kontraktu <see cref="HoneypotEvent"/>.
/// Pola wymagane: id, attackerIp, sensorId, timestamp, eventType.
/// Pozostałe pola są opcjonalne (zależą od typu sensora i zdarzenia).
/// </summary>
public sealed class HoneypotEventValidator : AbstractValidator<HoneypotEvent>
{
    public HoneypotEventValidator()
    {
        RuleFor(e => e.Id)
            .NotEmpty()
            .WithMessage("Pole 'id' jest wymagane i nie może być pustym GUID-em.");

        RuleFor(e => e.AttackerIp)
            .NotEmpty()
            .WithMessage("Pole 'attackerIp' jest wymagane.");

        RuleFor(e => e.SensorId)
            .NotEmpty()
            .WithMessage("Pole 'sensorId' jest wymagane.");

        RuleFor(e => e.Timestamp)
            .NotEqual(default(DateTimeOffset))
            .WithMessage("Pole 'timestamp' jest wymagane.");

        RuleFor(e => e.EventType)
            .IsInEnum()
            .WithMessage("Pole 'eventType' musi być jedną ze znanych wartości.");

        // Reguły warunkowe — spójność danych z typem zdarzenia.
        RuleFor(e => e.ThreatIntel!.Score)
            .InclusiveBetween(0, 100)
            .When(e => e.ThreatIntel?.Score is not null)
            .WithMessage("Pole 'threatIntel.score' musi być w zakresie 0–100.");

        RuleFor(e => e.Classification!.Sophistication)
            .InclusiveBetween(0.0, 1.0)
            .When(e => e.Classification?.Sophistication is not null)
            .WithMessage("Pole 'classification.sophistication' musi być w zakresie 0.0–1.0.");
    }
}
