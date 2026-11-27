using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The scope's view of the contexts configured onto the shared connection.
/// </summary>
public sealed class SharedContextProviderTests
{
    [Fact]
    public void Constructor_WithANullArgument_Throws()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();

        FluentActions.Invoking(() => new SharedContextProvider(null!, new SharedTransactionEnlistment()))
            .Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
        FluentActions.Invoking(() => new SharedContextProvider(serviceProvider, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("enlistment");
    }
}
