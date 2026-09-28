namespace Bsft.Core.Polling;

/// <summary>
/// Receives the three things a <see cref="IPollLoop"/> has to say about the passes it runs.
/// </summary>
/// <remarks>
/// The loop owns the control flow, the reporter owns the vocabulary: each service phrases these
/// events in its own words and at its own level, which is why the loop takes a reporter instead of a
/// logger. It also keeps this abstraction free of any logging dependency.
/// </remarks>
public interface IPollReporter
{
    /// <summary>
    /// Reports a pass that ended in a failure. The loop keeps running, so this is the only place the
    /// failure is seen.
    /// </summary>
    /// <param name="failure">
    /// The failure of the pass: the exception itself where the pass failed once, and a flattened
    /// <see cref="AggregateException"/> over all of them where it failed more than once.
    /// </param>
    void ReportPassFailed(Exception failure);

    /// <summary>
    /// Reports a pass that ended cancelled although nobody asked the service to stop — a store or
    /// command timeout, typically. The loop treats it as a failed pass and carries on.
    /// </summary>
    void ReportPassCanceledWithoutStop();

    /// <summary>Reports that the loop is unwinding because the service was asked to stop.</summary>
    void ReportStopping();
}
