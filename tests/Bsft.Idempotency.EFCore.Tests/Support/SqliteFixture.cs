using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Idempotency.EFCore.Tests.Support;

/// <summary>
/// A real SQLite database, private to one fixture, that a second connection can still see.
/// </summary>
/// <remarks>
/// <para>
/// A real provider rather than the in-memory one, because half of what these tests assert is provider
/// behaviour: the unique index on <c>(Scope, Key)</c> that refuses the second insert, the translation
/// of the set-based reclaim and completion writes, the lengths the columns carry. The in-memory
/// provider has none of that and would pass while the database refused.
/// </para>
/// <para>
/// The database is a <em>temp file</em> rather than an in-memory one. A bare <c>:memory:</c> database
/// is private to the single connection that opened it, so a race whose competing write must arrive on
/// a <em>second</em> connection would either see an empty database or run reentrantly on the one
/// connection. A file-backed database is shareable by independent connections on every platform, so
/// each context opens its own normal connection to the same file and the interceptor's competing
/// commit is the genuinely separate connection the race intends.
/// </para>
/// <para>
/// Connection pooling is off. It is not a performance choice: a pooled connection is only released back
/// to the operating system when the pool is cleared, and the only lever for that is
/// <c>SqliteConnection.ClearAllPools</c>, which is process-wide. Test classes in one assembly run in
/// parallel, so one fixture tearing down would reach into the pooled handles another class's running
/// test is holding, and the next connection that class took out of the pool came back disposed
/// (<c>ObjectDisposedException</c> on <c>SQLitePCL.sqlite3</c>). Unpooled, a closed connection really
/// closes, so the temp file is deletable without a global reach and no fixture can disturb another.
/// </para>
/// </remarks>
public sealed class SqliteFixture : IDisposable
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteFixture()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"bsft-idem-{Guid.NewGuid():n}.db");
        _connectionString = $"Data Source={_databasePath};Pooling=False";

        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    /// <summary>A context with its own connection to the shared database file.</summary>
    public TestIdempotencyDbContext NewContext() => new(Options());

    /// <summary>
    /// A context over the same database, on its own connection, with interceptors attached for forcing
    /// save failures or committing a competing write mid-save.
    /// </summary>
    public TestIdempotencyDbContext NewContextWith(params IInterceptor[] interceptors) =>
        new(Options(interceptors));

    private DbContextOptions<TestIdempotencyDbContext> Options(params IInterceptor[] interceptors) =>
        new DbContextOptionsBuilder<TestIdempotencyDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(interceptors)
            .Options;

    public void Dispose()
    {
        // Every connection to the file is unpooled and already closed, so nothing holds it open; the
        // try/catch then guarantees a locked-file teardown never fails the run regardless.
        Delete(_databasePath);
        Delete(_databasePath + "-wal");
        Delete(_databasePath + "-shm");
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
