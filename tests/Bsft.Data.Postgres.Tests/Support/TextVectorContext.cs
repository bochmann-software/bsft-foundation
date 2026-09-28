using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>An application that mapped the name to an ordinary text column.</summary>
public sealed class TextVectorContext : SearchTestContext
{
    protected override void MapVector(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Candidate>().Property<string>(BsftDataPostgresOptions.DefaultVectorProperty);
}
