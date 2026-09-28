using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application whose searchable entity is keyed on two columns.</summary>
public sealed class CompositeKeyContext : LongKeyContext
{
    protected override void Map(EntityTypeBuilder<Applicant> entity) =>
        entity.HasKey(a => new { a.Id, a.Name });
}
