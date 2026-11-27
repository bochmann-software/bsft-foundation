using System.Diagnostics;
using Bsft.Data.Tests.Support;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The listener that hands the open shared transaction to a context as it initializes, and leaves every
/// other context and every other event alone.
/// </summary>
public sealed class SharedContextLifetimeListenerTests
{
    [Fact]
    public void OnNext_ForAContextWithoutTheSharedTransaction_LeavesItAlone()
    {
        using var listener = new SharedContextLifetimeListener().Subscribe();
        using var connection = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<CatalogContext>().UseSqlite(connection).Options;

        using (var context = new CatalogContext(options))
        {
            context.Database.CurrentTransaction.Should().BeNull();
        }

        listener.OnNext(new KeyValuePair<string, object?>("unrelated", value: null));
        using var unrelated = new DiagnosticListener("Bsft.Data.Tests.Unrelated");
        listener.OnNext(unrelated);
    }

    [Fact]
    public void Subscribe_AfterDispose_KeepsNoSubscription()
    {
        var listener = new SharedContextLifetimeListener();
        listener.Dispose();

        var act = () => listener.Subscribe();

        act.Should().NotThrow();
        listener.OnCompleted();
        listener.OnError(new InvalidOperationException("never raised"));
    }

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Infrastructure.ContextInitialized", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Infrastructure.ContextDisposed", true)]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted", false)]
    public void IsLifetimeEvent_NamesTheContextLifetimeEventsOnly(string name, bool isLifetimeEvent) =>
        SharedContextLifetimeListener.IsLifetimeEvent(name).Should().Be(isLifetimeEvent);
}
