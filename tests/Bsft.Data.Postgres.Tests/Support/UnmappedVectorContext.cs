using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>An application that created the column but never mapped it.</summary>
public sealed class UnmappedVectorContext : SearchTestContext
{
    protected override void MapVector(ModelBuilder modelBuilder)
    {
    }
}
