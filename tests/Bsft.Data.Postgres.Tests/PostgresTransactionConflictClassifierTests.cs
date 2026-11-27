using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Bsft.Data.Postgres.Tests;

/// <summary>
/// Which PostgreSQL failures retry a whole shared transaction: the serialization failure and the
/// deadlock, wherever EF Core or a handler wrapped them, and nothing else.
/// </summary>
public sealed class PostgresTransactionConflictClassifierTests
{
    private readonly PostgresTransactionConflictClassifier _classifier = new();

    [Theory]
    [InlineData(PostgresErrorCodes.SerializationFailure)]
    [InlineData(PostgresErrorCodes.DeadlockDetected)]
    public void IsConflict_WhenTheServerRefusedTheTransactionForAConcurrentOne_IsTrue(string sqlState)
    {
        _classifier.IsConflict(Failure(sqlState)).Should().BeTrue();
    }

    [Fact]
    public void IsConflict_WhenEFCoreWrappedTheFailure_LooksThroughTheWrapper()
    {
        var wrapped = new InvalidOperationException(
            "handler", new DbUpdateException("save", Failure(PostgresErrorCodes.SerializationFailure)));

        _classifier.IsConflict(wrapped).Should().BeTrue();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.UniqueViolation)]
    [InlineData(PostgresErrorCodes.TooManyConnections)]
    public void IsConflict_WhenTheServerErrorIsAnyOther_IsFalse(string sqlState)
    {
        _classifier.IsConflict(Failure(sqlState)).Should().BeFalse();
    }

    [Fact]
    public void IsConflict_WhenTheFailureIsNotTheServer_IsFalse()
    {
        _classifier.IsConflict(new TimeoutException()).Should().BeFalse();
    }

    [Fact]
    public void IsConflict_WithNullException_Throws()
    {
        var act = () => _classifier.IsConflict(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static PostgresException Failure(string sqlState) => new("refused", "ERROR", "ERROR", sqlState);
}
