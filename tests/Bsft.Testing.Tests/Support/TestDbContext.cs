using Microsoft.EntityFrameworkCore;

namespace Bsft.Testing.Tests.Support;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<TestNote> Notes => Set<TestNote>();
}
