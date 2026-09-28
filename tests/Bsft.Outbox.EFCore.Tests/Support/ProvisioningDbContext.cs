using Microsoft.EntityFrameworkCore;

namespace Bsft.Outbox.EFCore.Tests.Support;

/// <summary>The context the second outbox's rows are written and drained through.</summary>
public sealed class ProvisioningDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="ProvisioningDbContext"/> class.</summary>
    /// <param name="options">The options the context is configured with.</param>
    public ProvisioningDbContext(DbContextOptions<ProvisioningDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the provisioning intents waiting to be carried out.</summary>
    public DbSet<ProvisioningIntentRow> Intents => Set<ProvisioningIntentRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        var intents = modelBuilder.Entity<ProvisioningIntentRow>();

        intents.ToTable("ProvisioningIntents");
        intents.HasKey(e => e.Id);
        intents.Property(e => e.TenantId).HasMaxLength(64).IsRequired();
        intents.Property(e => e.FailureReason).HasMaxLength(ProvisioningIntentRowSource.ReasonMaxLength);
        intents.HasIndex(e => new { e.ProcessedAt, e.Id });
    }
}
