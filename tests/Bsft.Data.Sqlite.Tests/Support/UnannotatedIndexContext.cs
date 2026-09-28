using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An index mapped by hand, without the column list the ranking weights are checked against.</summary>
public sealed class UnannotatedIndexContext : SearchTestContext
{
    protected override void MapIndex(ModelBuilder modelBuilder) =>
        modelBuilder.SharedTypeEntity<BsftFullTextIndexRow>("Candidates_fts", entity =>
        {
            entity.HasNoKey();
            entity.ToTable("Candidates_fts", t => t.ExcludeFromMigrations());
            entity.Property(r => r.RowId).HasColumnName("rowid");
            entity.Property(r => r.Match).HasColumnName("Candidates_fts");
        });
}
