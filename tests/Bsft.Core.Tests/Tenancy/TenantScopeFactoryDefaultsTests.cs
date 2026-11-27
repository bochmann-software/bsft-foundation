using Bsft.Core.Tenancy;
using FluentAssertions;
using Xunit;

namespace Bsft.Core.Tests.Tenancy;

/// <summary>
/// The default implementation of CreateScopeAsync checks the cancellation token and delegates to CreateScope,
/// ensuring backward compatibility with implementations that only override CreateScope.
/// </summary>
public class TenantScopeFactoryDefaultsTests
{
    /// <summary>
    /// Default falls back to CreateScope exactly once with the same tenant id and returns that scope.
    /// </summary>
    [Fact]
    public async Task CreateScopeAsync_WithDefaultImplementation_CallsCreateScopeOnceAndReturnsScope()
    {
        var fakeFactory = new FakeTenantScopeFactory();
        ITenantScopeFactory factory = fakeFactory;

        var scope = await factory.CreateScopeAsync("a", CancellationToken.None);

        fakeFactory.CreateScopeCalls.Should().Equal("a");
        fakeFactory.CreateScopeAsyncCalls.Should().BeEmpty();
        scope.Should().NotBeNull();
    }

    /// <summary>
    /// A cancelled token throws OperationCanceledException and CreateScope is NOT called.
    /// </summary>
    [Fact]
    public async Task CreateScopeAsync_WithCancelledToken_ThrowsAndDoesNotCallCreateScope()
    {
        var fakeFactory = new FakeTenantScopeFactory();
        ITenantScopeFactory factory = fakeFactory;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await factory.CreateScopeAsync("a", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fakeFactory.CreateScopeCalls.Should().BeEmpty();
    }

    /// <summary>
    /// An ArgumentException thrown by CreateScope propagates.
    /// </summary>
    [Fact]
    public async Task CreateScopeAsync_WhenCreateScopeThrows_PropagatesTheException()
    {
        var fakeFactory = new FakeTenantScopeFactory { ThrowOnCreateScope = true };
        ITenantScopeFactory factory = fakeFactory;

        var act = async () => await factory.CreateScopeAsync("a", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// A fake ITenantScopeFactory that only implements CreateScope for testing the default.
    /// </summary>
    private sealed class FakeTenantScopeFactory : ITenantScopeFactory
    {
        public List<string> CreateScopeCalls { get; } = new();
        public List<string> CreateScopeAsyncCalls { get; } = new();
        public bool ThrowOnCreateScope { get; set; }

        public ITenantScope CreateScope(string tenantId)
        {
            CreateScopeCalls.Add(tenantId);

            if (ThrowOnCreateScope)
                throw new ArgumentException("test error");

            return new FakeScope();
        }

        private sealed class FakeScope : ITenantScope
        {
            public string TenantId => "fake";
            public IServiceProvider ServiceProvider => throw new NotImplementedException();

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;
        }
    }
}
