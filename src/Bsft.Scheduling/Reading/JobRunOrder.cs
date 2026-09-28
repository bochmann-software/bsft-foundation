namespace Bsft.Scheduling.Reading;

/// <summary>
/// How a listing of job runs is ordered.
/// </summary>
/// <remarks>
/// A short closed set rather than a sort expression, because each member names a column the run table
/// is indexed on. Every order ends in the identity, so a page boundary falls in the same place twice.
/// <para>
/// There is deliberately no order by when a run <em>started</em>. The row records when the run was
/// due, when its lease expires and when it settled; nothing writes a start instant, and inventing one
/// from the lease would be a guess that reads like a fact.
/// </para>
/// </remarks>
public enum JobRunOrder
{
    /// <summary>Most recently due first, which is the newest work.</summary>
    DueDescending = 0,

    /// <summary>Least recently due first, which is the oldest work still outstanding.</summary>
    DueAscending = 1,

    /// <summary>Most recently settled first, with a run that has not settled last.</summary>
    SettledDescending = 2,

    /// <summary>Least recently settled first, with a run that has not settled last.</summary>
    SettledAscending = 3,

    /// <summary>Soonest-expiring lease first, with a run nobody holds last.</summary>
    LeaseExpiry = 4,
}
