using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bsft.Data.Sqlite.Tests.Support;

/// <summary>An application whose searchable entity has no key at all.</summary>
public sealed class KeylessContext : LongKeyContext
{
    protected override void Map(EntityTypeBuilder<Applicant> entity) =>
        entity.HasNoKey().ToTable("Applicants");
}
