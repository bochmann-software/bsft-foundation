using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application whose index columns do not line up with the group's fields.</summary>
public sealed class SwappedColumnsContext : SearchTestContext
{
    protected override void MapIndex(ModelBuilder modelBuilder) =>
        modelBuilder.HasBsftFullTextIndex("Candidates_fts", "Notes", "Name");
}
