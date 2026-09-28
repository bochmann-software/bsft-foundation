using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests;

/// <summary>
/// The framework's unit of work over the host's own context: what it commits, what it fences, and
/// what it deliberately leaves to a caller who already opened a transaction.
/// </summary>
public sealed class DbContextUnitOfWorkTests
{
    [Fact]
    public void WithNullContextThrows()
    {
        var act = () => new DbContextUnitOfWork<TestDbContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task CommitAsyncSavesWhatTheScopeStaged()
    {
        await using var context = InMemoryContext();
        context.TestEntities.Add(new TestEntity { Id = 1, Name = "staged" });

        await new DbContextUnitOfWork<TestDbContext>(context).CommitAsync();

        context.ChangeTracker.Entries().Should().OnlyContain(entry => entry.State == EntityState.Unchanged);
    }

    [Fact]
    public async Task ExecuteAsyncWithNullWorkThrows()
    {
        await using var context = InMemoryContext();

        var act = () => new DbContextUnitOfWork<TestDbContext>(context).ExecuteAsync<int>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// A provider with no transactions runs the work as it is, which is the same honesty the in-memory
    /// stores state about themselves rather than a guarantee this class cannot keep.
    /// </summary>
    [Fact]
    public async Task OnANonRelationalProviderTheWorkSimplyRuns()
    {
        await using var context = InMemoryContext();

        var answer = await new DbContextUnitOfWork<TestDbContext>(context).ExecuteAsync(async ct =>
        {
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "written" });
            await context.SaveChangesAsync(ct);

            return 42;
        });

        answer.Should().Be(42);
        (await context.TestEntities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task OpensAndCommitsATransactionOfItsOwn()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = SqliteContext(connection);

        var answer = await new DbContextUnitOfWork<TestDbContext>(context).ExecuteAsync(async ct =>
        {
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "durable" });
            await context.SaveChangesAsync(ct);

            return "done";
        });

        answer.Should().Be("done");
        context.Database.CurrentTransaction.Should().BeNull("the unit ends where it was opened");

        await using var reader = SqliteContext(connection);
        (await reader.TestEntities.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DiscardsTheWritesOfAUnitThatThrows()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = SqliteContext(connection);

        var act = () => new DbContextUnitOfWork<TestDbContext>(context).ExecuteAsync<int>(async ct =>
        {
            context.TestEntities.Add(new TestEntity { Id = 1, Name = "doomed" });
            await context.SaveChangesAsync(ct);

            throw new InvalidOperationException("the unit failed");
        });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("the unit failed");

        await using var reader = SqliteContext(connection);
        (await reader.TestEntities.CountAsync()).Should().Be(0);
    }

    /// <summary>
    /// A transaction the host already opened is joined, so the caller's rollback still undoes
    /// everything the unit wrote. A second transaction here would have committed half of it.
    /// </summary>
    [Fact]
    public async Task JoinsATransactionTheCallerAlreadyOpened()
    {
        await using var connection = await OpenConnectionAsync();
        await using var context = SqliteContext(connection);

        await using (var callerTransaction = await context.Database.BeginTransactionAsync())
        {
            await new DbContextUnitOfWork<TestDbContext>(context).ExecuteAsync(async ct =>
            {
                context.TestEntities.Add(new TestEntity { Id = 1, Name = "the caller's" });
                await context.SaveChangesAsync(ct);

                return 0;
            });

            context.Database.CurrentTransaction.Should().BeSameAs(callerTransaction);

            await callerTransaction.RollbackAsync();
        }

        await using var reader = SqliteContext(connection);
        (await reader.TestEntities.CountAsync()).Should().Be(0);
    }

    private static TestDbContext InMemoryContext() =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    /// <summary>
    /// One connection per test, kept open for its lifetime: a SQLite in-memory database exists only
    /// while a connection to it does, and the second context here has to see the first one's rows.
    /// </summary>
    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using var schema = SqliteContext(connection);
        await schema.Database.EnsureCreatedAsync();

        return connection;
    }

    private static TestDbContext SqliteContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(connection).Options);
}
