using Bsft.Scheduling.EFCore.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Scheduling.EFCore.Tests.Fakes;

/// <summary>
/// A job context whose save always fails, standing in for a persistent write problem rather than
/// the benign race: the reads still reach the database, so the store can tell the two apart.
/// </summary>
internal sealed class FailingSaveJobDbContext : IJobDbContext
{
    private readonly TestJobDbContext _inner;

    public FailingSaveJobDbContext(TestJobDbContext inner) => _inner = inner;

    public DbSet<JobRunEntity> JobRuns => _inner.JobRuns;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        throw new DbUpdateException("the job name does not fit the column");
}
