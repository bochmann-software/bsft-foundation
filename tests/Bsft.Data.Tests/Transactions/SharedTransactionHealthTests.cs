using Bsft.Core.Persistence;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The providers' view of the shared transaction: conflict classification and the abort probe.
/// </summary>
public sealed class SharedTransactionHealthTests
{
    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        var enlistment = new SharedTransactionEnlistment();

        FluentActions.Invoking(() => new SharedTransactionHealth(null!, [], []))
            .Should().Throw<ArgumentNullException>().WithParameterName("enlistment");
        FluentActions.Invoking(() => new SharedTransactionHealth(enlistment, null!, []))
            .Should().Throw<ArgumentNullException>().WithParameterName("classifiers");
        FluentActions.Invoking(() => new SharedTransactionHealth(enlistment, [], null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("probes");
    }

    [Fact]
    public void IsConflict_WhenAConflictIsWrappedAsInnerException_RecognizesIt()
    {
        var enlistment = new SharedTransactionEnlistment();
        var conflictException = new TimeoutException("conflict");
        var classifier = new FakeTimeoutClassifier();
        var health = new SharedTransactionHealth(enlistment, [classifier], []);

        // The conflict is an InnerException of an AmbientTransactionMisuseException
        var wrappedFailure = new AmbientTransactionMisuseException("wrapped", conflictException);

        health.IsConflict(wrappedFailure).Should().BeTrue("the classifier finds the conflict in the inner exception chain");
    }

    [Fact]
    public void IsConflict_WhenNoConflictIsInTheChain_ReturnsFalse()
    {
        var enlistment = new SharedTransactionEnlistment();
        var classifier = new FakeTimeoutClassifier();
        var health = new SharedTransactionHealth(enlistment, [classifier], []);

        var unrelatedFailure = new AmbientTransactionMisuseException("wrapped", new SqliteException("unrelated", 1));

        health.IsConflict(unrelatedFailure).Should().BeFalse("no classifier recognizes the inner exception");
    }

    [Fact]
    public void FindConflict_WhenOnlyTheRecordedCommandFailureIsAConflict_ReturnsIt()
    {
        var enlistment = new SharedTransactionEnlistment();
        var classifier = new FakeTimeoutClassifier();
        var health = new SharedTransactionHealth(enlistment, [classifier], []);

        // Create a timeout exception to use as the recorded failure
        var recordedTimeout = new TimeoutException("recorded failure");

        // Set up the enlistment with a recorded failure
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        enlistment.Begin(transaction);
        enlistment.RecordFailure(recordedTimeout);

        // When there is no exception passed, but the enlistment has a recorded failure,
        // FindConflict should return the recorded failure if it matches the classifier
        var result = health.FindConflict(new InvalidOperationException("unrelated"));

        result.Should().BeSameAs(recordedTimeout, "the recorded command failure should be found when it matches the classifier");
    }

    /// <summary>A test classifier that recognizes TimeoutException as a conflict.</summary>
    private sealed class FakeTimeoutClassifier : ITransactionConflictClassifier
    {
        public bool IsConflict(Exception exception) => exception is TimeoutException;
    }
}
