using Bsft.Data.Postgres.Tests.Support;
using Bsft.Tests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// The question the shared transaction asks PostgreSQL right before it commits: has the server already
/// aborted the transaction.
/// </summary>
/// <remarks>
/// Every fact but the null guard is reported as skipped without a server; set
/// <c>BSFT_TEST_POSTGRES</c> to run them.
/// </remarks>
public sealed class PostgresTransactionAbortProbeTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private readonly PostgresTransactionAbortProbe _probe = new();

    [PostgresFact]
    public async Task FindAbortAsync_WhenTheTransactionCanCommit_FindsNone()
    {
        await using var connection = await OpenAsync("probe_healthy");
        await using var transaction = await connection.BeginTransactionAsync();

        (await _probe.FindAbortAsync(transaction)).Should().BeNull();
    }

    [PostgresFact]
    public async Task FindAbortAsync_WhenAStatementFailedInTheTransaction_ReturnsTheServerError()
    {
        await using var connection = await OpenAsync("probe_aborted");
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var failing = new NpgsqlCommand("SELECT 1 / 0", connection, transaction))
        {
            var act = () => failing.ExecuteScalarAsync();
            await act.Should().ThrowAsync<PostgresException>();
        }

        var abort = await _probe.FindAbortAsync(transaction);

        abort.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.InFailedSqlTransaction);
    }

    [PostgresFact]
    public async Task FindAbortAsync_WhenTheTransactionWasDisposed_Throws()
    {
        await using var connection = await OpenAsync("probe_disposed");
        var transaction = await connection.BeginTransactionAsync();
        await transaction.DisposeAsync();

        var act = () => _probe.FindAbortAsync(transaction);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FindAbortAsync_WithNullTransaction_Throws()
    {
        var act = () => _probe.FindAbortAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    private async Task<NpgsqlConnection> OpenAsync(string purpose)
    {
        var connection = new NpgsqlConnection(await fixture.CreateDatabaseAsync(purpose));
        await connection.OpenAsync();

        return connection;
    }
}
