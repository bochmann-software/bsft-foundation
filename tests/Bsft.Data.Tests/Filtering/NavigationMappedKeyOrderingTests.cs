using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Filtering;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Filtering;

/// <summary>
/// Pins that a sort field mapped through a navigation to a related type's key does not suppress the
/// entity's own key tie-breaker, even when both key members are named <c>Id</c>.
/// </summary>
public class NavigationMappedKeyOrderingTests : IDisposable
{
    private readonly DocumentContext _context;

    public NavigationMappedKeyOrderingTests()
    {
        var options = new DbContextOptionsBuilder<DocumentContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new DocumentContext(options);

        // One owner, three documents inserted out of key order: an ordering that actually runs is
        // distinguishable from the order the source happens to return rows in.
        var owner = new Owner { Id = 5 };
        _context.Documents.AddRange(
            new Document { Id = 3, Owner = owner },
            new Document { Id = 1, Owner = owner },
            new Document { Id = 2, Owner = owner });
        _context.SaveChanges();
    }

    [Fact]
    public async Task QueryExecutor_WhenASortFieldMapsThroughANavigationToAKeyNamedId_StillBreaksTheTieOnTheEntityKey()
    {
        // OwnerId maps to Owner.Id, whose member is named "Id" exactly like Document's own key.
        // Comparing the leaf member name alone would treat the sort as already ordering by the key and
        // skip the tie-breaker; comparing member identity keeps it, so documents with the same owner
        // still page in a fixed Document.Id order.
        var executor = new QueryExecutor<Document, DocumentDto>(new OwnerIdSortConfiguration());

        var result = await executor.ExecuteAsync(
            _context.Documents,
            new PagedRequest(Sort: [new SortItem("OwnerId", SortDirection.Asc)], Page: 1, PageSize: 10));

        result.Items.Select(d => d.Id).Should().Equal(1, 2, 3);
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A configuration whose sortable field maps through a navigation to the related key.</summary>
    private sealed class OwnerIdSortConfiguration : QueryConfigurationBase<Document, DocumentDto>
    {
        public override void Configure(QueryConfigurationBuilder<Document, DocumentDto> builder) =>
            builder.Field(d => d.OwnerId).MapsTo(e => e.Owner.Id).AllowSort();
    }

    private sealed class Owner
    {
        public int Id { get; set; }
    }

    private sealed class Document
    {
        public int Id { get; set; }

        public int OwnerId { get; set; }

        public Owner Owner { get; set; } = null!;
    }

    private sealed record DocumentDto(int Id, int OwnerId);

    private sealed class DocumentContext : DbContext
    {
        public DocumentContext(DbContextOptions<DocumentContext> options) : base(options)
        {
        }

        public DbSet<Document> Documents => Set<Document>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder is null)
                throw new ArgumentNullException(nameof(modelBuilder));

            modelBuilder.Entity<Document>().HasKey(d => d.Id);
            modelBuilder.Entity<Document>().HasOne(d => d.Owner).WithMany().HasForeignKey(d => d.OwnerId);
        }
    }
}
