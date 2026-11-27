using Bsft.Data.DependencyInjection;
using Bsft.Data.Tests.Support;
using Bsft.Data.Transactions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.Transactions;

/// <summary>
/// The one registration the shared transaction cannot live with: the single-context unit of work,
/// which would open a second transaction on the shared connection.
/// </summary>
public sealed class SharedTransactionStartupValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenOnlyTheSharedTransactionIsRegistered_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddBsftDataSharedTransaction(_ => throw new NotSupportedException());

        var result = await new SharedTransactionStartupValidator(services).ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenTheSingleContextUnitOfWorkIsRegisteredToo_FailsNamingTheContext()
    {
        var services = new ServiceCollection();
        services.AddBsftDataUnitOfWork<CatalogContext>();
        services.AddBsftDataSharedTransaction(_ => throw new NotSupportedException());

        var result = await new SharedTransactionStartupValidator(services).ValidateAsync();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("AddBsftDataUnitOfWork<CatalogContext>");
    }

    [Fact]
    public void Constructor_WithNullServices_Throws()
    {
        var act = () => new SharedTransactionStartupValidator(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
