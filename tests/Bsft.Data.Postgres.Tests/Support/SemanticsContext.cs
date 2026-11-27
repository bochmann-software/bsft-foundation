using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Test DbContext for filter semantics testing, mapping SemanticsRow with jsonb column.
/// </summary>
public class SemanticsContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="SemanticsContext"/> class.</summary>
    /// <param name="options">The DbContext options.</param>
    public SemanticsContext(DbContextOptions<SemanticsContext> options)
        : base(options)
    {
    }

    /// <summary>Gets or sets the semantics rows.</summary>
    public DbSet<SemanticsRow> Rows { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<SemanticsRow>(entity =>
        {
            entity.ToTable("Rows");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Size);
            entity.Property(e => e.Label);

            // Map Custom as jsonb column
            entity.Property(e => e.Custom)
                .HasColumnType("jsonb")
                .IsRequired();
        });
    }
}
