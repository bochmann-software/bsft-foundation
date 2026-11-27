using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>The guard against a shared transaction configured from the root provider.</summary>
public sealed class SharedTransactionRootTests
{
    [Fact]
    public void Constructor_WithNullProvider_Throws() =>
        FluentActions.Invoking(() => new SharedTransactionRoot(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("rootProvider");

    [Fact]
    public void ThrowIfRoot_WithAScopedProvider_Passes()
    {
        using var root = new ServiceCollection().BuildServiceProvider();
        using var scope = root.CreateScope();
        var guard = new SharedTransactionRoot(root);

        FluentActions.Invoking(() => guard.ThrowIfRoot(scope.ServiceProvider, typeof(object))).Should().NotThrow();
        FluentActions.Invoking(() => guard.ThrowIfRoot(root, typeof(object))).Should().Throw<InvalidOperationException>();
    }
}
