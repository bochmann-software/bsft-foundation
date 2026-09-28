using Bsft.Core;
using Bsft.Mediator.Transactions;
using Bsft.Tests.Shared;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Reflection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// What a committed dispatch leaves behind besides the data: the log line saying so, and the one
/// shared resolver whose type cache the container's singleton exists for.
/// </summary>
public class TransactionBehaviorCommitTests
{
    [Fact]
    public async Task HandleAsync_WhenTheHandlerSucceeds_LogsTheCommitForTheRequest()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();

        var logger = new RecordingLogger<TransactionBehavior<TransactionalTestCommand, string>>();
        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(provider, logger);

        await behavior.HandleAsync(new TransactionalTestCommand("tx"), () => Task.FromResult("committed"));

        logger.Messages.Should().ContainSingle(message => message.StartsWith("[Transactional] Committed transaction", StringComparison.Ordinal))
            .Which.Should().Contain(nameof(TransactionalTestCommand));
    }

    [Fact]
    public async Task HandleAsync_WithTheResolverRegistered_ResolvesTheContextThroughTheRegisteredResolver()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var resolver = new TransactionDbContextResolver();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<TransactionTestDbContext>(options => options.UseSqlite(connection));
        services.AddSingleton<ITransactionDbContextResolver>(resolver);
        using var provider = services.BuildServiceProvider();

        var behavior = new TransactionBehavior<TransactionalTestCommand, string>(
            provider, provider.GetRequiredService<ILogger<TransactionBehavior<TransactionalTestCommand, string>>>());

        await behavior.HandleAsync(new TransactionalTestCommand("tx"), () => Task.FromResult("committed"));

        // The registered singleton did the resolving, so its cache is the one every dispatch shares;
        // a private fallback instance would have left it untouched.
        resolver.TargetTypeResolutions.Should().Be(1);
    }
}
