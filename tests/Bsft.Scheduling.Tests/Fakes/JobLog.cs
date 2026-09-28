namespace Bsft.Scheduling.Tests.Fakes;

/// <summary>Records every execution so a test can assert what the scheduler actually ran.</summary>
internal sealed class JobLog
{
    private readonly List<JobExecutionContext> _executions = [];
    private readonly object _gate = new();

    public IReadOnlyList<JobExecutionContext> Executions
    {
        get
        {
            lock (_gate)
                return _executions.ToList();
        }
    }

    public int Count => Executions.Count;

    public void Record(JobExecutionContext context)
    {
        lock (_gate)
            _executions.Add(context);
    }
}
