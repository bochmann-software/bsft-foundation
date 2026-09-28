namespace Bsft.Scheduling;

/// <summary>
/// Default <see cref="IJobCorrelationAccessor"/> that only knows the ambient id of a job currently
/// executing. It is what a host without <c>Bsft.Scheduling.Correlation</c> gets: a job enqueued from
/// another job keeps that job's correlation id, and one enqueued from a request carries none.
/// </summary>
internal sealed class AmbientJobCorrelationAccessor : IJobCorrelationAccessor
{
    /// <inheritdoc />
    public string? CorrelationId => JobCorrelationScope.Current;
}
