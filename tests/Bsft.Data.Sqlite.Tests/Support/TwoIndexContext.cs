using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application with two searchable entities, each with its own index.</summary>
public sealed class TwoIndexContext : LongKeyContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasBsftFullTextIndex("Applicants_fts", "Name", "Notes");
    }
}
