using FluentAssertions;
using Xunit;

namespace Bsft.EventAggregator.Saga.Tests;

public class InMemorySagaStoreTests
{
    private readonly InMemorySagaStore _sut = new();

    public class TestState
    {
        public string Value { get; set; } = string.Empty;
    }

    [Fact]
    public async Task SaveAsync_And_GetAsync_RoundTrips()
    {
        var instance = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "hello" });

        await _sut.SaveAsync(instance);
        var result = await _sut.FindAsync<TestState>("TestSaga", "key-1");

        result.Should().NotBeNull();
        result!.State.Value.Should().Be("hello");
        result.SagaType.Should().Be("TestSaga");
        result.CorrelationKey.Should().Be("key-1");
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var result = await _sut.FindAsync<TestState>("TestSaga", "no-such-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingInstance()
    {
        var instance1 = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "first" });
        var instance2 = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "second" });

        await _sut.SaveAsync(instance1);
        await _sut.SaveAsync(instance2);

        var result = await _sut.FindAsync<TestState>("TestSaga", "key-1");
        result!.State.Value.Should().Be("second");
    }

    [Fact]
    public async Task RemoveAsync_DeletesInstance()
    {
        var instance = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "hello" });
        await _sut.SaveAsync(instance);

        await _sut.DeleteAsync("TestSaga", "key-1");

        var result = await _sut.FindAsync<TestState>("TestSaga", "key-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistent_DoesNotThrow()
    {
        var act = async () => await _sut.DeleteAsync("TestSaga", "no-such-key");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConcurrentSaveGetRemove_DoesNotThrowAndKeepsConsistentState()
    {
        // The store is backed by a ConcurrentDictionary; concurrent operations on distinct keys
        // must complete without corruption or exceptions.
        const int count = 200;

        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(async () =>
        {
            var key = $"key-{i}";
            await _sut.SaveAsync(new SagaInstance<TestState>("TestSaga", key, new TestState { Value = key }));
            var loaded = await _sut.FindAsync<TestState>("TestSaga", key);
            loaded!.State.Value.Should().Be(key);
            await _sut.DeleteAsync("TestSaga", key);
        }));

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();

        (await _sut.FindAsync<TestState>("TestSaga", "key-0")).Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_DifferentSagaTypes_ReturnsCorrectInstance()
    {
        var instance1 = new SagaInstance<TestState>("SagaA", "key-1", new TestState { Value = "A" });
        var instance2 = new SagaInstance<TestState>("SagaB", "key-1", new TestState { Value = "B" });

        await _sut.SaveAsync(instance1);
        await _sut.SaveAsync(instance2);

        var resultA = await _sut.FindAsync<TestState>("SagaA", "key-1");
        var resultB = await _sut.FindAsync<TestState>("SagaB", "key-1");

        resultA!.State.Value.Should().Be("A");
        resultB!.State.Value.Should().Be("B");
    }

    [Fact]
    public async Task GetAsync_NullSagaType_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.FindAsync<TestState>(null!, "key-1");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("sagaType");
    }

    [Fact]
    public async Task GetAsync_NullCorrelationKey_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.FindAsync<TestState>("TestSaga", null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("correlationKey");
    }

    [Fact]
    public async Task SaveAsync_NullInstance_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.SaveAsync<TestState>(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("instance");
    }

    [Fact]
    public async Task RemoveAsync_NullSagaType_ThrowsArgumentNullException()
    {
        var act = async () => await _sut.DeleteAsync(null!, "key-1");

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("sagaType");
    }

    [Fact]
    public async Task GetAsync_AfterTheSavedStateWasMutated_StillAnswersWithWhatWasSaved()
    {
        // A step mutates its state in place and the commit is what makes that permanent. If the store
        // held the caller's own object, a step that threw half way through would have left its changes
        // in what the store calls the last committed state, and the next event would read them.
        var instance = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "saved" });
        await _sut.SaveAsync(instance);

        instance.State.Value = "never committed";
        instance.CurrentStepIndex = 7;

        var loaded = await _sut.FindAsync<TestState>("TestSaga", "key-1");

        loaded!.State.Value.Should().Be("saved");
        loaded.CurrentStepIndex.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_TwiceOverOneSavedInstance_HandsOutSeparateStates()
    {
        // Two readers of one row are two readers of one row in any real store, not two names for the
        // same object, and a test that passes here has to pass there too.
        var instance = new SagaInstance<TestState>("TestSaga", "key-1", new TestState { Value = "saved" });
        await _sut.SaveAsync(instance);

        var first = await _sut.FindAsync<TestState>("TestSaga", "key-1");
        var second = await _sut.FindAsync<TestState>("TestSaga", "key-1");

        first!.State.Value = "touched";

        second!.State.Value.Should().Be("saved");
    }

    [Fact]
    public async Task RemoveAsync_NullCorrelationKey_ThrowsArgumentNullException()
    {
        // Exercises InMemorySagaStore.RemoveAsync line 46: null correlationKey guard
        var act = async () => await _sut.DeleteAsync("TestSaga", null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("correlationKey");
    }
}
