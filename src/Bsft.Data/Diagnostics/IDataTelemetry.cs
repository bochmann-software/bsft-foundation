namespace Bsft.Data.Diagnostics;

/// <summary>
/// Emits metrics for data access. A degrading full-text index should be visible in a dashboard
/// before users start complaining that search is slow.
/// </summary>
public interface IDataTelemetry
{
    /// <summary>Records a completed search.</summary>
    /// <param name="group">The search group that was queried.</param>
    /// <param name="seconds">The elapsed duration in seconds.</param>
    /// <param name="resultCount">The total number of matches.</param>
    void SearchCompleted(string group, double seconds, int resultCount);
}
