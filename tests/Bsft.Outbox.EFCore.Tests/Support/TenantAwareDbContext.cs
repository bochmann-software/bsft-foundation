using Microsoft.EntityFrameworkCore;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>The context for tenant-aware intent rows.</summary>
public sealed class TenantAwareDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TenantAwareDbContext"/> class.</summary>
    /// <param name="options">The options the context is configured with.</param>
    public TenantAwareDbContext(DbContextOptions<TenantAwareDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the tenant-aware provisioning intents waiting to be carried out.</summary>
    public DbSet<TenantAwareIntentRow> Intents => Set<TenantAwareIntentRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        var intents = modelBuilder.Entity<TenantAwareIntentRow>();

        intents.ToTable("TenantAwareIntents");
        intents.HasKey(e => e.Id);
        intents.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
        intents.Property(e => e.IntentName).HasMaxLength(64).IsRequired();
        intents.Property(e => e.FailureReason).HasMaxLength(TenantAwareIntentRowSource.ReasonMaxLength);
        intents.HasIndex(e => new { e.TenantId, e.ProcessedAt, e.Id });
    }
}
