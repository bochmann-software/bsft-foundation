using Bsft.Scheduling.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Fakes;

/// <summary>
/// A job context that lets a competing writer slip in between the store's successor check and the
/// update that acts on it. The set is handed out once per statement, so the second hand-out is the
/// update, and the race is reproducible instead of a matter of timing.
/// </summary>
internal sealed class RetryRacingJobDbContext : IJobDbContext
{
    private readonly TestJobDbContext _inner;
    private readonly Func<Task> _beforeSecondStatement;
    private int _handedOut;

    public RetryRacingJobDbContext(TestJobDbContext inner, Func<Task> beforeSecondStatement)
    {
        _inner = inner;
        _beforeSecondStatement = beforeSecondStatement;
    }

    public DbSet<JobRunEntity> JobRuns
    {
        get
        {
            if (++_handedOut == 2)
                _beforeSecondStatement().GetAwaiter().GetResult();

            return _inner.JobRuns;
        }
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _inner.SaveChangesAsync(ct);
}
