using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Tests.Support;

public sealed class CandidateContext : DbContext
{
    public CandidateContext(DbContextOptions<CandidateContext> options) : base(options)
    {
    }

    public DbSet<Candidate> Candidates => Set<Candidate>();
}
