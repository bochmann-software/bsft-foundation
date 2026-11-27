using Bsft.Caching;
using Bsft.Caching.Invalidation;
using Bsft.Caching.Keys;
using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.Transactions;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.Transactions;
using Bsft.Resilience.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// The default chain over the shared transaction of <c>Bsft.Data</c>: a command and the command it
/// dispatches to another module, each writing through its own context on one SQLite database, commit
/// or roll back as one, and <c>[Retryable]</c> re-runs the whole outermost attempt.
/// </summary>
public sealed class SharedTransactionScenarioTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"bsft-mediator-shared-{Guid.NewGuid():N}.db");
    private readonly ScenarioLog _log = new();

    public SharedTransactionScenarioTests()
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE Orders(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);" +
            "CREATE TABLE Reservations(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    private string ConnectionString => $"Data Source={_path};Pooling=False";

    [Fact]
    public async Task SendAsync_WhenTheNestedCommandSucceeds_CommitsBothModulesOnce()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        Count("Orders").Should().Be(1);
        Count("Reservations").Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenTheNestedCommandFails_RollsBackTheOuterModuleToo()
    {
        _log.IsReservationRefused = true;
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        await act.Should().ThrowAsync<BusinessRuleException>();
        _log.OrderAttempts.Should().Be(1, "a deterministic failure is not retried");
        Count("Orders").Should().Be(0);
        Count("Reservations").Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WhenTheNestedCommandConflicts_RetriesTheWholeOutermostUnit()
    {
        _log.ConflictingReservations = 1;
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        attempts.Should().Be(2);
        _log.ReservationAttempts.Should().Be(2, "the nested command is re-run with its outer attempt, never on its own");
        Count("Orders").Should().Be(1, "the order the first attempt left tracked was cleared, not inserted again");
        Count("Reservations").Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenTheNestedCommandConflicts_RunsTheRetryInAFreshScopeSeededBeforeTheConnection()
    {
        _log.ConflictingReservations = 1;
        await using var provider = BuildProvider(services =>
        {
            services.AddScoped<ScopeMarker>();
            services.AddScoped<IRequestScopeSeed, ScopeMarkerSeed>();
        });
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeMarker>().Name = "tenant-a";

        var attempts = await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        attempts.Should().Be(2);
        _log.ConnectionMarkers.Should().Equal(["tenant-a", "tenant-a-seeded"],
            "every attempt has a connection of its own, created after the seed carried the tenant across");
        Count("Orders").Should().Be(1);
        Count("Reservations").Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenEveryCommandIsRetriedButNotEveryCommandIsATransaction_RetriesOnlyTheOutermost()
    {
        _log.ConflictingReservations = 1;
        await using var provider = BuildProvider(isRetryingAllCommands: true);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new ForwardReservationCommand("bolt"));

        _log.ForwardAttempts.Should().Be(2, "the nested conflict reached the outer attempt, which ran again");
        _log.ReservationAttempts.Should().Be(2, "the nested command was not retried on its own inside the outer attempt");
        Count("Reservations").Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenEveryAttemptConflicts_GivesUpAfterTheRetryBudget()
    {
        _log.ConflictingReservations = int.MaxValue;
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediator>().SendAsync(new PlaceOrderCommand("bolt"));

        await act.Should().ThrowAsync<ScenarioConflictException>();
        _log.OrderAttempts.Should().Be(3, "one attempt plus the two retries configured");
        Count("Orders").Should().Be(0);
    }

    [Fact]
    public async Task ValidateAsync_WithSeveralContextsAndTheSharedTransaction_StartsWithoutABaseDbContext()
    {
        await using var provider = BuildProvider();

        var results = await Task.WhenAll(provider.GetServices<IStartupValidator>()
            .OfType<TransactionDbContextValidator>()
            .Select(validator => validator.ValidateAsync()));

        results.Should().ContainSingle().Which.IsValid.Should().BeTrue();
    }

    public void Dispose() => File.Delete(_path);

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure = null, bool isRetryingAllCommands = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_log);
        // The caching behavior of the default chain is activated for every request; these requests are
        // not cacheable, so substitutes are all it needs.
        services.AddSingleton(Substitute.For<ICacheStore>());
        services.AddSingleton(Substitute.For<ICacheKeyBuilder>());
        services.AddSingleton(Substitute.For<ICacheTagIndex>());
        services.AddSingleton(TimeProvider.System);
        services.AddBsftMediator(mediator => mediator
            .RegisterServicesFromAssembly(typeof(PlaceOrderCommandHandler).Assembly)
            .UseDefaults());
        // A predicate that retries nothing on its own, so a retry here is the provider's classification.
        services.AddBsftResilienceMediator(options =>
        {
            options.MaxRetryAttempts = 2;
            options.BaseDelay = TimeSpan.Zero;
            options.ShouldRetry = _ => false;
            options.ShouldRetryAllCommands = isRetryingAllCommands;
        });
        services.AddBsftDataSharedTransaction(serviceProvider =>
        {
            _log.ConnectionMarkers.Add(serviceProvider.GetService<ScopeMarker>()?.Name ?? "none");

            return new SqliteConnection(ConnectionString);
        });
        services.AddSingleton<ITransactionConflictClassifier, ScenarioConflictClassifier>();
        services.AddDbContext<OrderingScenarioDbContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection)));
        services.AddDbContext<StockScenarioDbContext>((sp, options) => options
            .UseBsftSharedTransaction(sp, (builder, connection) => builder.UseSqlite(connection)));
        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    }

    private long Count(string table)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";

        return (long)command.ExecuteScalar()!;
    }
}
