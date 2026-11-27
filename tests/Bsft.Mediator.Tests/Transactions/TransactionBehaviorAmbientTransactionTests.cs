using Bsft.Core.Persistence;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// With an <see cref="IAmbientTransaction"/> registered the behavior hands a transactional request to
/// it as a unit and resolves no <c>DbContext</c>; a request that is not transactional never reaches it.
/// </summary>
public sealed class TransactionBehaviorAmbientTransactionTests
{
    [Theory]
    [InlineData(false, "Beginning the shared transaction")]
    [InlineData(true, "Joining the shared transaction")]
    public async Task HandleAsync_WhenAnAmbientTransactionIsRegistered_RunsTheRequestAsAUnitOfIt(bool isActive, string logged)
    {
        var ambient = Substitute.For<IAmbientTransaction>();
        ambient.IsActive.Returns(isActive);
        ambient.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<string>>>()(CancellationToken.None));
        var logger = new RecordingLogger<TransactionBehavior<TransactionalTestCommand, string>>();
        using var provider = new ServiceCollection().AddSingleton(ambient).BuildServiceProvider();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(provider, logger);

        var result = await behavior.HandleAsync(new TransactionalTestCommand("tx"), () => Task.FromResult("handled"));

        result.Should().Be("handled");
        await ambient.Received(1).ExecuteAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>());
        logger.Messages.Should().Contain(message => message.Contains(logged));
    }

    [Fact]
    public async Task HandleAsync_WhenTheRequestIsNotTransactional_LeavesTheAmbientTransactionAlone()
    {
        var ambient = Substitute.For<IAmbientTransaction>();
        using var provider = new ServiceCollection().AddSingleton(ambient).BuildServiceProvider();
        var behavior = new TransactionBehavior<NoTransactionCommand, string>(
            provider, Substitute.For<ILogger<TransactionBehavior<NoTransactionCommand, string>>>());

        var result = await behavior.HandleAsync(new NoTransactionCommand("plain"), () => Task.FromResult("handled"));

        result.Should().Be("handled");
        ambient.ReceivedCalls().Should().BeEmpty();
    }
}
