namespace Bsft.Scheduling;

/// <summary>
/// Controls what happens to runs that became due while no instance was alive to execute them.
/// </summary>
public enum JobCatchUpPolicy
{
    /// <summary>
    /// Every overdue run is executed as soon as an instance picks it up, however late. This is the
    /// default, because silently dropping work is the more surprising behaviour.
    /// </summary>
    RunAll = 0,

    /// <summary>
    /// A run that is overdue by more than <see cref="BsftSchedulingOptions.MaxCatchUpAge"/> is marked
    /// skipped instead of executed. Use it for work whose value expires — a nightly report that
    /// nobody wants three days late.
    /// </summary>
    SkipBeyondMaxAge = 1,
}
