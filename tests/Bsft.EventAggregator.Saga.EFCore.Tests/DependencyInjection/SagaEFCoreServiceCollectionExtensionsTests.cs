using Bsft.EventAggregator.Saga.DependencyInjection;
using Bsft.EventAggregator.Saga.EFCore.DependencyInjection;
using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using Bsft.EventAggregator.Saga.Timeouts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests.DependencyInjection;

public class SagaEFCoreServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftSagaEFCore_RegistersEFCoreSagaStore()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-store-test"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCore<TestSagaDbContext>();

        // The ISagaStore should now be resolvable as EFCoreSagaStore
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Ensure DB created
        var dbContext = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
        dbContext.Database.EnsureCreated();

        var store = provider.GetService<ISagaStore>();
        store.Should().NotBeNull().And.BeOfType<EFCoreSagaStore>();
    }

    [Fact]
    public void TheTimeoutConfiguration_MakesThePendingNameIndexUnique()
    {
        var model = BuildModel(new SagaTimeoutEntityConfiguration());

        var index = model.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "SagaType", "CorrelationKey", "Name" }));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be(SagaTimeoutEntityConfiguration.DefaultPendingFilter);
    }

    [Fact]
    public void AssemblyScanningAppliesTheTimeoutConfiguration()
    {
        // ApplyConfigurationsFromAssembly matches a parameterless constructor exactly, and an
        // all-optional one does not match it. Skipped in silence, the timeouts table would keep its
        // name through the DbSet convention while losing the unique pending-name index that keeps
        // two instances from both inserting the same timer and firing the reminder twice.
        using var db = new ScannedSagaDbContext(
            new DbContextOptionsBuilder<ScannedSagaDbContext>().UseSqlite("DataSource=:memory:").Options);

        var index = db.Model.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "SagaType", "CorrelationKey", "Name" }));

        index.IsUnique.Should().BeTrue();
        index.GetFilter().Should().Be(SagaTimeoutEntityConfiguration.DefaultPendingFilter);
    }

    [Fact]
    public void AProviderWithoutFilteredIndexes_CanDropTheFilterAndWithItTheUniqueness()
    {
        var model = BuildModel(new SagaTimeoutEntityConfiguration(pendingFilter: null));

        var indexes = model.FindEntityType(typeof(SagaTimeoutEntity))!.GetIndexes().ToList();

        indexes.Should().OnlyContain(i => !i.IsUnique && i.GetFilter() == null);
    }

    [Fact]
    public void ACustomFilterIsAppliedVerbatim()
    {
        var model = BuildModel(new SagaTimeoutEntityConfiguration("\"DispatchedAt\" IS NULL"));

        var indexes = model.FindEntityType(typeof(SagaTimeoutEntity))!.GetIndexes().ToList();

        // Two of the three carry the filter: the due index the dispatcher polls and the unique
        // pending-name index. The third answers the operator listing, which reads retired rows too,
        // so a pending filter would exclude exactly what that listing is for.
        indexes.Where(i => i.GetFilter() is not null)
            .Should().HaveCount(2)
            .And.OnlyContain(i => i.GetFilter() == "\"DispatchedAt\" IS NULL");
    }

    [Fact]
    public void TheInstanceHistoryIndex_IsUnfilteredSoARetiredTimeoutStaysOnIt()
    {
        // EFCoreSagaTimeoutReader.ListForAsync reads every timeout of one instance ordered by due
        // date. Only an unfiltered index over those columns can serve it; the table only grows,
        // because a dispatched or dead-lettered row is never purged.
        var model = BuildModel(new SagaTimeoutEntityConfiguration());

        var historyIndex = model.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "SagaType", "CorrelationKey", "DueAt", "Id" }));

        historyIndex.GetFilter().Should().BeNull();
        historyIndex.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void WithoutAFilter_TheNameIndexAlreadyAnswersTheInstanceHistoryLookup()
    {
        // No second index is declared then: the name index is unfiltered and leads with the same two
        // columns, so the lookup is served from its prefix.
        var model = BuildModel(new SagaTimeoutEntityConfiguration(pendingFilter: null));

        model.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Should().NotContain(i => i.Properties.Select(p => p.Name)
                .SequenceEqual(new[] { "SagaType", "CorrelationKey", "DueAt", "Id" }));
    }

    [Fact]
    public void TheDueIndex_CarriesTheIdTieBreakerTheDispatcherOrdersOn()
    {
        // ListDueAsync orders by DueAt and then by Id. Without the tie-breaker in the index key a
        // backlog has to be sorted in full before the first batch-sized page can be returned.
        var filtered = BuildModel(new SagaTimeoutEntityConfiguration());

        var dueIndex = filtered.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Single(i => i.Properties.Select(p => p.Name).SequenceEqual(new[] { "DueAt", "Id" }));

        dueIndex.GetFilter().Should().Be(SagaTimeoutEntityConfiguration.DefaultPendingFilter);

        var unfiltered = BuildModel(new SagaTimeoutEntityConfiguration(pendingFilter: null));

        unfiltered.FindEntityType(typeof(SagaTimeoutEntity))!
            .GetIndexes()
            .Should().ContainSingle(i =>
                i.Properties.Select(p => p.Name).SequenceEqual(new[] { "DispatchedAt", "DueAt", "Id" }));
    }

    private static Microsoft.EntityFrameworkCore.Metadata.IModel BuildModel(SagaTimeoutEntityConfiguration configuration)
    {
        var builder = new ModelBuilder();
        configuration.Configure(builder.Entity<SagaTimeoutEntity>());
        return builder.FinalizeModel();
    }

    [Fact]
    public void AddBsftSagaEFCoreWithTimeouts_RegistersEFCoreSagaTimeoutStore()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-timeout-store-test"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<Timeouts.ISagaTimeoutStore>()
            .Should().NotBeNull().And.BeOfType<EFCoreSagaTimeoutStore>();
        services.Should().Contain(sd => sd.ServiceType == typeof(ISagaTimeoutDbContext) && sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddBsftSagaEFCore_PersistsStateOnly_AndLeavesTimeoutsInMemory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-state-only-test"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCore<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<ISagaStore>().Should().BeOfType<EFCoreSagaStore>();
        provider.GetService<Timeouts.ISagaTimeoutStore>().Should().BeOfType<Timeouts.InMemorySagaTimeoutStore>();
    }

    [Fact]
    public void AddBsftSagaEFCore_RegistersISagaDbContext_AsScoped()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-ctx-test"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCore<TestSagaDbContext>();

        // ISagaDbContext should be registered as scoped
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISagaDbContext) &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddBsftSagaEFCore_ReturnsTheServiceCollection_ForChaining()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-chain-test"));
        services.AddBsftSaga();

        services.AddBsftSagaEFCore<TestSagaDbContext>().Should().BeSameAs(services);
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>().Should().BeSameAs(services);
    }

    [Fact]
    public void AddBsftSagaEFCore_NullServices_Throws()
    {
        var act = () => SagaEFCoreServiceCollectionExtensions.AddBsftSagaEFCore<TestSagaDbContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftSagaEFCoreWithTimeouts_NullServices_Throws()
    {
        var act = () => SagaEFCoreServiceCollectionExtensions.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddBsftSagaEFCoreWithTimeouts_ReplacesThePersistenceWindow()
    {
        // The in-memory default commits every write on its own; the EF Core window is what makes a
        // step's state advance and its timeout writes share one context, so it has to win over the
        // TryAdd that AddBsftSaga made.
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-window-test"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Persistence.ISagaPersistenceWindow>()
            .Should().BeOfType<EFCoreSagaPersistenceWindow>();
    }

    [Fact]
    public void AddBsftSagaEFCore_AStoreRegisteredOnTheRelationalSeam_IsWhatISagaStoreResolvesTo()
    {
        // The README offers IEFCoreSagaStore as the seam for replacing exactly the relational store.
        // That only holds while ISagaStore resolves through it rather than constructing its own.
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-seam-state"));
        services.AddSingleton<IEFCoreSagaStore>(new CountingSagaStore());

        services.AddBsftSaga();
        services.AddBsftSagaEFCore<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISagaStore>().Should().BeOfType<CountingSagaStore>();
    }

    [Fact]
    public void AddBsftSagaEFCoreWithTimeouts_BothRelationalSeams_AreWhatTheEngineContractsResolveTo()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-seam-both"));
        services.AddSingleton<IEFCoreSagaStore>(new CountingSagaStore());
        services.AddSingleton<IEFCoreSagaTimeoutStore>(new CountingTimeoutStore());

        services.AddBsftSaga();
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISagaStore>().Should().BeOfType<CountingSagaStore>();
        provider.GetRequiredService<ISagaTimeoutStore>().Should().BeOfType<CountingTimeoutStore>();
    }

    [Fact]
    public void AddBsftSagaEFCoreWithTimeouts_WithoutAHostRegistration_RegistersTheEFCoreStores()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase("saga-ext-seam-default"));

        services.AddBsftSaga();
        services.AddBsftSagaEFCoreWithTimeouts<TestSagaDbContext>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IEFCoreSagaStore>().Should().BeOfType<EFCoreSagaStore>();
        provider.GetRequiredService<IEFCoreSagaTimeoutStore>().Should().BeOfType<EFCoreSagaTimeoutStore>();
        provider.GetRequiredService<ISagaTimeoutStore>().Should().BeOfType<EFCoreSagaTimeoutStore>();
    }

    [Fact]
    public void TheStateConfiguration_BoundsTheColumnsItIndexes()
    {
        // An unbounded string maps to nvarchar(max) on SQL Server, which refuses it as an index key
        // column — the unique instance index could not be created at all.
        var builder = new ModelBuilder();
        new SagaStateEntityConfiguration().Configure(builder.Entity<SagaStateEntity>());
        var entity = builder.FinalizeModel().FindEntityType(typeof(SagaStateEntity))!;

        entity.FindProperty(nameof(SagaStateEntity.SagaType))!.GetMaxLength().Should().Be(200);
        entity.FindProperty(nameof(SagaStateEntity.CorrelationKey))!.GetMaxLength().Should().Be(200);
        entity.FindProperty(nameof(SagaStateEntity.StateTypeName))!.GetMaxLength().Should().Be(500);
    }

    /// <summary>A relational saga store the host brought along; only its identity is under test.</summary>
    private sealed class CountingSagaStore : IEFCoreSagaStore
    {
        public Task<SagaInstance<TState>?> FindAsync<TState>(string sagaType, string correlationKey, CancellationToken ct = default) =>
            Task.FromResult<SagaInstance<TState>?>(null);

        public Task SaveAsync<TState>(SagaInstance<TState> instance, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(string sagaType, string correlationKey, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>A relational timeout store the host brought along; only its identity is under test.</summary>
    private sealed class CountingTimeoutStore : IEFCoreSagaTimeoutStore
    {
        public bool IsStagingIntoWindow => false;

        public Task ScheduleAsync(SagaTimeout timeout, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAsync(string sagaType, string correlationKey, string name, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelForStepAsync(string sagaType, string correlationKey, int stepIndex, CancellationToken ct = default) => Task.CompletedTask;

        public Task CancelAllAsync(string sagaType, string correlationKey, CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<SagaTimeout>> ListDueAsync(DateTimeOffset now, int batchSize, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SagaTimeout>>([]);

        public Task<bool> TryDispatchAsync(
            SagaTimeout timeout,
            DateTimeOffset now,
            Func<SagaTimeout, CancellationToken, Task> publish,
            CancellationToken ct = default) => Task.FromResult(false);

        public Task SkipAsync(IReadOnlyCollection<long> ids, DateTimeOffset now, CancellationToken ct = default) => Task.CompletedTask;

        public Task RecordFailedAttemptAsync(long id, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeadLetterAsync(long id, DateTimeOffset now, string reason, CancellationToken ct = default) => Task.CompletedTask;
    }
}
