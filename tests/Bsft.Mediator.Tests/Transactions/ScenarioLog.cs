namespace Bsft.Mediator.Tests.Transactions;

/// <summary>Counts the handler runs of one scenario and decides which of them fail.</summary>
public sealed class ScenarioLog
{
    public int OrderAttempts { get; set; }

    public int ReservationAttempts { get; set; }

    public int ForwardAttempts { get; set; }

    /// <summary>The scope marker each shared connection was created under, in order.</summary>
    public List<string> ConnectionMarkers { get; } = [];

    /// <summary>How many reservation runs, counted from the first, fail with a conflict.</summary>
    public int ConflictingReservations { get; set; }

    /// <summary>Whether a reservation fails with a deterministic error instead.</summary>
    public bool IsReservationRefused { get; set; }
}
