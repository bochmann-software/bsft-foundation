using Bsft.Scheduling.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Fakes;

/// <summary>
/// A job context that lets a competing writer slip in between the store's existence check and its
/// insert, so the race the unique index guards against becomes reproducible.
/// </summary>
internal sealed class RacingJobDbContext : IJobDbContext
{
    private readonly TestJobDbContext _inner;
    private readonly Func<Task> _beforeFirstSave;
    private bool _raced;

    public RacingJobDbContext(TestJobDbContext inner, Func<Task> beforeFirstSave)
    {
        _inner = inner;
        _beforeFirstSave = beforeFirstSave;
    }

    public DbSet<JobRunEntity> JobRuns => _inner.JobRuns;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (!_raced)
        {
            _raced = true;
            await _beforeFirstSave();
        }

        return await _inner.SaveChangesAsync(ct);
    }
}
