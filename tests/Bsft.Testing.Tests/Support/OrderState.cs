using Bsft.EventAggregator.Saga;

namespace Bsft.Testing.Tests.Support;

public record OrderState
{
    public int Reminders { get; set; }

    public bool IsPaid { get; set; }
}
