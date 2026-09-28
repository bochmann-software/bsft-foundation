using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application that created the index but forgot to map it.</summary>
public sealed class UnmappedIndexContext : SearchTestContext
{
    protected override void MapIndex(ModelBuilder modelBuilder)
    {
    }
}
