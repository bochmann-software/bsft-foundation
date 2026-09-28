using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

/// <summary>A context for an entity whose key is not named by convention.</summary>
public sealed class TicketContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="TicketContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    public TicketContext(DbContextOptions<TicketContext> options) : base(options)
    {
    }

    /// <summary>Gets the ticket set.</summary>
    public DbSet<Ticket> Tickets => Set<Ticket>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder is null)
            throw new ArgumentNullException(nameof(modelBuilder));

        modelBuilder.Entity<Ticket>().HasKey(t => t.Code);
    }
}
