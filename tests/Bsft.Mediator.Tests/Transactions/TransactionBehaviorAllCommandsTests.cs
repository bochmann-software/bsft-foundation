using Bsft.Core.Persistence;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// With <see cref="MediatorConfiguration.ApplyTransactionToAllCommands"/> every command is
/// transactional without the attribute, queries are not, and a command dispatched inside an open
/// behavior transaction runs in it instead of beginning a second one.
/// </summary>
public sealed class TransactionBehaviorAllCommandsTests
{
    [Fact]
    public async Task HandleAsync_WithThePolicy_RunsAnUnmarkedCommandInTheAmbientTransaction()
    {
        var ambient = AmbientTransaction();
        using var provider = new ServiceCollection().AddSingleton(ambient).BuildServiceProvider();
        var behavior = Behavior<NoTransactionCommand>(provider, Policy());

        var result = await behavior.HandleAsync(new NoTransactionCommand("plain"), () => Task.FromResult("handled"));

        result.Should().Be("handled");
        await ambient.Received(1).ExecuteAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithoutThePolicy_LeavesAnUnmarkedCommandAlone()
    {
        var ambient = AmbientTransaction();
        using var provider = new ServiceCollection().AddSingleton(ambient).BuildServiceProvider();
        var behavior = Behavior<NoTransactionCommand>(provider, new MediatorConfiguration());

        await behavior.HandleAsync(new NoTransactionCommand("plain"), () => Task.FromResult("handled"));

        ambient.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithThePolicy_LeavesAnUnmarkedQueryAlone()
    {
        var ambient = AmbientTransaction();
        using var provider = new ServiceCollection().AddSingleton(ambient).BuildServiceProvider();
        var behavior = Behavior<TestQuery>(provider, Policy());

        await behavior.HandleAsync(new TestQuery(1), () => Task.FromResult("read"));

        ambient.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WithThePolicy_RunsANestedCommandInTheOpenBehaviorTransaction()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<TransactionTestDbContext>();
        var outerLogger = new RecordingLogger<TransactionBehavior<NoTransactionCommand, string>>();
        var innerLogger = new RecordingLogger<TransactionBehavior<TransactionalTestCommand, string>>();
        var nesting = new RequestNesting();
        var outer = new TransactionBehavior<NoTransactionCommand, string>(provider, outerLogger, Policy(), new RequestMetadataProvider(), nesting);
        var inner = new TransactionBehavior<TransactionalTestCommand, string>(provider, innerLogger, Policy(), new RequestMetadataProvider(), nesting);
        object? outerTransaction = null;
        object? innerTransaction = null;

        var result = await outer.HandleAsync(new NoTransactionCommand("outer"), async () =>
        {
            outerTransaction = context.Database.CurrentTransaction;
            return await inner.HandleAsync(new TransactionalTestCommand("inner"), () =>
            {
                innerTransaction = context.Database.CurrentTransaction;
                return Task.FromResult("nested");
            });
        });

        result.Should().Be("nested");
        outerTransaction.Should().NotBeNull();
        innerTransaction.Should().BeSameAs(outerTransaction);
        innerLogger.Messages.Should().BeEmpty("the nested command neither began nor committed a transaction");
        outerLogger.Messages.Should().Contain(message => message.StartsWith("[Transactional] Committed transaction", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_WithThePolicy_BeginsATransactionOfItsOwnForACommandOfAnotherScope()
    {
        using var outerConnection = new SqliteConnection("DataSource=:memory:");
        using var innerConnection = new SqliteConnection("DataSource=:memory:");
        outerConnection.Open();
        innerConnection.Open();
        using var outerProvider = new ServiceCollection()
            .AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(outerConnection))
            .BuildServiceProvider();
        using var innerProvider = new ServiceCollection()
            .AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(innerConnection))
            .BuildServiceProvider();
        var innerLogger = new RecordingLogger<TransactionBehavior<TransactionalTestCommand, string>>();
        var outer = new TransactionBehavior<NoTransactionCommand, string>(
            outerProvider, Substitute.For<ILogger<TransactionBehavior<NoTransactionCommand, string>>>(), Policy(), new RequestMetadataProvider(), new RequestNesting());
        var inner = new TransactionBehavior<TransactionalTestCommand, string>(
            innerProvider, innerLogger, Policy(), new RequestMetadataProvider(), new RequestNesting());
        object? innerTransaction = null;

        await outer.HandleAsync(new NoTransactionCommand("outer"), () =>
            inner.HandleAsync(new TransactionalTestCommand("inner"), () =>
            {
                innerTransaction = innerProvider.GetRequiredService<TransactionTestDbContext>().Database.CurrentTransaction;
                return Task.FromResult("nested");
            }));

        innerTransaction.Should().NotBeNull("the scope of the inner command holds no transaction open yet");
        innerLogger.Messages.Should().Contain(message => message.StartsWith("[Transactional] Committed transaction", StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var logger = Substitute.For<ILogger<TransactionBehavior<NoTransactionCommand, string>>>();

        FluentActions.Invoking(() => new TransactionBehavior<NoTransactionCommand, string>(
                provider, logger, null!, new RequestMetadataProvider(), new RequestNesting()))
            .Should().Throw<ArgumentNullException>().WithParameterName("transactionPolicy");
        FluentActions.Invoking(() => new TransactionBehavior<NoTransactionCommand, string>(
                provider, logger, Policy(), null!, new RequestNesting()))
            .Should().Throw<ArgumentNullException>().WithParameterName("metadataProvider");
        FluentActions.Invoking(() => new TransactionBehavior<NoTransactionCommand, string>(
                provider, logger, Policy(), new RequestMetadataProvider(), null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("nesting");
    }

    [Fact]
    public async Task ApplyTransactionToAllCommands_ReachesTheBehaviorTheContainerBuilds()
    {
        var ambient = AmbientTransaction();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(ambient);
        services.AddBsftMediator(mediator => mediator
            .AddBehavior(typeof(TransactionBehavior<,>))
            .ApplyTransactionToAllCommands());
        using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var provider = scope.ServiceProvider;

        var behavior = provider.GetServices<IPipelineBehavior<NoTransactionCommand, string>>()
            .OfType<TransactionBehavior<NoTransactionCommand, string>>()
            .Single();
        await behavior.HandleAsync(new NoTransactionCommand("plain"), () => Task.FromResult("handled"));

        await ambient.Received(1).ExecuteAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>());
    }

    private static MediatorConfiguration Policy() => new MediatorConfiguration().ApplyTransactionToAllCommands();

    private static TransactionBehavior<TRequest, string> Behavior<TRequest>(
        IServiceProvider provider, MediatorConfiguration configuration) =>
        new(provider, Substitute.For<ILogger<TransactionBehavior<TRequest, string>>>(), configuration, new RequestMetadataProvider(), new RequestNesting());


    private static IAmbientTransaction AmbientTransaction()
    {
        var ambient = Substitute.For<IAmbientTransaction>();
        ambient.ExecuteAsync(Arg.Any<Func<CancellationToken, Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<string>>>()(CancellationToken.None));
        return ambient;
    }
}
