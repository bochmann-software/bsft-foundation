using Bsft.EventAggregator.Saga.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.EventAggregator.Saga.EFCore.Tests;

public class EFCoreSagaStoreTests
{
    private static (EFCoreSagaStore Store, ServiceProvider Provider) CreateStore(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestSagaDbContext>(opt => opt.UseInMemoryDatabase(dbName));
        services.AddScoped<ISagaDbContext>(sp => sp.GetRequiredService<TestSagaDbContext>());

        var provider = services.BuildServiceProvider();

        // Ensure the database is created.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
            db.Database.EnsureCreated();
        }

        var store = new EFCoreSagaStore(provider.GetRequiredService<IServiceScopeFactory>());
        return (store, provider);
    }

    [Fact]
    public async Task SaveAndGet_RoundTripsAllProperties()
    {
        var (store, provider) = CreateStore(nameof(SaveAndGet_RoundTripsAllProperties));
        using var _ = provider;

        var orderId = Guid.NewGuid();
        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-1", new TestSagaState { OrderId = orderId, Paid = true })
        {
            CurrentStepIndex = 2,
            IsComplete = false
        };

        await store.SaveAsync(instance);

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "order-1");

        loaded.Should().NotBeNull();
        loaded!.SagaType.Should().Be("OrderSaga");
        loaded.CorrelationKey.Should().Be("order-1");
        loaded.State.OrderId.Should().Be(orderId);
        loaded.State.Paid.Should().BeTrue();
        loaded.CurrentStepIndex.Should().Be(2);
        loaded.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Save_UpdatesExistingRow()
    {
        var (store, provider) = CreateStore(nameof(Save_UpdatesExistingRow));
        using var _ = provider;

        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-1", new TestSagaState { OrderId = Guid.NewGuid() })
        {
            CurrentStepIndex = 0
        };
        await store.SaveAsync(instance);

        instance.CurrentStepIndex = 3;
        instance.State.Paid = true;
        await store.SaveAsync(instance);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
        var count = await db.SagaStates.CountAsync(e => e.SagaType == "OrderSaga" && e.CorrelationKey == "order-1");
        count.Should().Be(1);

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "order-1");
        loaded!.CurrentStepIndex.Should().Be(3);
        loaded.State.Paid.Should().BeTrue();
    }

    [Fact]
    public async Task Get_WithTamperedStateTypeName_ReturnsNull()
    {
        // A stored type that is not a TestSagaState must be ignored, not deserialized/instantiated.
        var (store, provider) = CreateStore(nameof(Get_WithTamperedStateTypeName_ReturnsNull));
        using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
            db.SagaStates.Add(new SagaStateEntity
            {
                SagaType = "OrderSaga",
                CorrelationKey = "tampered",
                StateTypeName = typeof(string).AssemblyQualifiedName!,
                StateJson = "\"hacked\"",
                CurrentStepIndex = 0,
                IsComplete = false
            });
            await db.SaveChangesAsync();
        }

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "tampered");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Remove_DeletesExistingEntry()
    {
        var (store, provider) = CreateStore(nameof(Remove_DeletesExistingEntry));
        using var _ = provider;

        var instance = new SagaInstance<TestSagaState>("OrderSaga", "order-1", new TestSagaState());
        await store.SaveAsync(instance);

        await store.DeleteAsync("OrderSaga", "order-1");

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "order-1");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Get_NonExistentKey_ReturnsNull()
    {
        var (store, provider) = CreateStore(nameof(Get_NonExistentKey_ReturnsNull));
        using var _ = provider;

        var loaded = await store.FindAsync<TestSagaState>("OrderSaga", "unknown-key");
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Get_WithUnresolvableStateTypeName_ReturnsNull()
    {
        // Exercises line 43-44: stateType is null when StateTypeName can't be resolved
        var (store, provider) = CreateStore(nameof(Get_WithUnresolvableStateTypeName_ReturnsNull));
        using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
            db.Database.EnsureCreated();
            db.SagaStates.Add(new SagaStateEntity
            {
                SagaType = "UnknownSaga",
                CorrelationKey = "key-1",
                StateTypeName = "Some.Assembly.ThatDoesNotExist.StateType, NonExistentAssembly",
                StateJson = "{}",
                CurrentStepIndex = 0,
                IsComplete = false
            });
            await db.SaveChangesAsync();
        }

        var result = await store.FindAsync<TestSagaState>("UnknownSaga", "key-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Get_WithNullDeserializedState_ReturnsNull()
    {
        // Exercises lines 47-48: deserialization returns null when StateJson is "null"
        var (store, provider) = CreateStore(nameof(Get_WithNullDeserializedState_ReturnsNull));
        using var _ = provider;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestSagaDbContext>();
            db.Database.EnsureCreated();
            db.SagaStates.Add(new SagaStateEntity
            {
                SagaType = "OrderSaga",
                CorrelationKey = "key-null",
                StateTypeName = typeof(TestSagaState).AssemblyQualifiedName!,
                StateJson = "null",  // Deserializes to null for reference types
                CurrentStepIndex = 0,
                IsComplete = false
            });
            await db.SaveChangesAsync();
        }

        var result = await store.FindAsync<TestSagaState>("OrderSaga", "key-null");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DifferentSagaTypes_AreIsolated()
    {
        var (store, provider) = CreateStore(nameof(DifferentSagaTypes_AreIsolated));
        using var _ = provider;

        var orderInstance = new SagaInstance<TestSagaState>("OrderSaga", "key-1", new TestSagaState { OrderId = Guid.NewGuid() });
        var otherInstance = new SagaInstance<OtherSagaState>("OtherSaga", "key-1", new OtherSagaState { Name = "test" });

        await store.SaveAsync(orderInstance);
        await store.SaveAsync(otherInstance);

        var loadedOrder = await store.FindAsync<TestSagaState>("OrderSaga", "key-1");
        var loadedOther = await store.FindAsync<OtherSagaState>("OtherSaga", "key-1");

        loadedOrder.Should().NotBeNull();
        loadedOrder!.State.OrderId.Should().Be(orderInstance.State.OrderId);

        loadedOther.Should().NotBeNull();
        loadedOther!.State.Name.Should().Be("test");
    }
}
