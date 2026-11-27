using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The interceptor that enlists a context in the scope's open shared transaction.
/// </summary>
public sealed class SharedTransactionInterceptorTests
{
    [Fact]
    public void Constructor_WithNullEnlistment_Throws()
    {
        var act = () => new SharedTransactionInterceptor(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("enlistment");
    }

    [Fact]
    public void CommandCanceled_RecordsTheFirstCancellationOfTheOpenTransactionOnly()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var enlistment = new SharedTransactionEnlistment();
        var interceptor = new SharedTransactionInterceptor(enlistment);

        interceptor.CommandCanceled(connection.CreateCommand(), null!);
        enlistment.Failure.Should().BeNull("no unit is open");

        enlistment.Begin(transaction);
        interceptor.CommandCanceled(connection.CreateCommand(), null!);
        interceptor.CommandCanceled(connection.CreateCommand(), null!);

        enlistment.Failure.Should().BeOfType<OperationCanceledException>();
    }
}
