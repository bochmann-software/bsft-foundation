namespace Bsft.Scheduling;

/// <summary>
/// The <see cref="IJobRunResultRecorder"/> of a <see cref="JobExecutionContext"/> that the scheduler did
/// not create, such as one a test builds by hand.
/// </summary>
/// <remarks>
/// It refuses rather than discards. A job under test that records a result and sees it vanish would
/// pass for the wrong reason; the refusal tells the test to supply a recorder of its own.
/// <para>
/// It holds no state and no dependency, which is why it is a record: a value that
/// <see cref="JobExecutionContext"/> can create as its default without that being the hidden
/// construction of a collaborator, and that compares equal to any other instance, so two hand-built
/// contexts describing the same run stay equal. One instance is shared all the same, because the
/// executor replaces it on every execution.
/// </para>
/// </remarks>
internal sealed record DetachedJobRunResultRecorder : IJobRunResultRecorder
{
    /// <inheritdoc />
    public void Record<TResult>(string schema, TResult result) =>
        throw new InvalidOperationException(
            $"This {nameof(JobExecutionContext)} was not created by the scheduler, so there is no run to record a result on. Set {nameof(JobExecutionContext.ResultRecorder)} to a recorder of your own to capture it.");
}
