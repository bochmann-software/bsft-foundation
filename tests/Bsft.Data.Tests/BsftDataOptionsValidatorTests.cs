using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests;

public class BsftDataOptionsValidatorTests
{
    [Fact]
    public void SectionName_IsTheDocumentedOne() =>
        BsftDataOptions.SectionName.Should().Be("Bsft:Data");

    [Fact]
    public void Validate_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new BsftDataOptionsValidator().Validate(name: null, options: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Validate_Defaults_Succeeds() =>
        new BsftDataOptionsValidator()
            .Validate(name: null, new BsftDataOptions())
            .Succeeded.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(int.MaxValue)]
    public void Validate_KeysetMaxPageSizeOutOfRange_Fails(int maxPageSize)
    {
        var options = new BsftDataOptions();
        options.KeysetPaging.MaxPageSize = maxPageSize;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("KeysetPaging.MaxPageSize").And.Contain($"'{maxPageSize}'");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue - 1)]
    public void Validate_KeysetMaxPageSizeAtItsBounds_Succeeds(int maxPageSize)
    {
        var options = new BsftDataOptions();
        options.KeysetPaging.MaxPageSize = maxPageSize;

        new BsftDataOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void KeysetPaging_Default_IsTheOffsetSurfacesMaximum() =>
        new BsftDataOptions().KeysetPaging.MaxPageSize.Should().Be(DataDefaults.KeysetMaxPageSize).And.Be(100);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankHeaderName_Fails(string headerName)
    {
        var options = new BsftDataOptions();
        options.MultiTenancy.HeaderName = headerName;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("HeaderName").And.Contain("must not be blank");
    }

    [Theory]
    [InlineData("X Tenant")]
    [InlineData("X-Tenant:Id")]
    [InlineData("X\tTenant")]
    public void Validate_HeaderNameThatIsNotAnHttpToken_Fails(string headerName)
    {
        var options = new BsftDataOptions();
        options.MultiTenancy.HeaderName = headerName;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("valid HTTP header name");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankClaimType_Fails(string claimType)
    {
        var options = new BsftDataOptions();
        options.MultiTenancy.ClaimType = claimType;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ClaimType").And.Contain("must not be blank");
    }

    [Fact]
    public void Validate_ClaimTypeThatIsAUri_Succeeds()
    {
        // A claim type is routinely a URI; only the header name has a grammar to keep.
        var options = new BsftDataOptions();
        options.MultiTenancy.ClaimType = "http://schemas.example.com/tenant";

        new BsftDataOptionsValidator().Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void ValidateOptionsResult_IsTheContractTheOptionsInfrastructureExpects() =>
        new BsftDataOptionsValidator().Should().BeAssignableTo<IValidateOptions<BsftDataOptions>>();

    [Theory]
    [InlineData(-1)]
    [InlineData(4096)]
    public void Validate_TenantKeysNodeIdOutOfRange_Fails(int nodeId)
    {
        var options = new BsftDataOptions();
        options.TenantKeys.NodeId = nodeId;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("NodeId").And.Contain("must be between 0");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4095)]
    public void Validate_TenantKeysNodeIdInRange_Succeeds(int nodeId)
    {
        var options = new BsftDataOptions();
        options.TenantKeys.NodeId = nodeId;

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_TenantKeysNodeIdNull_Succeeds()
    {
        var options = new BsftDataOptions();
        // NodeId defaults to null, so no explicit assignment needed

        var result = new BsftDataOptionsValidator().Validate(name: null, options);

        result.Succeeded.Should().BeTrue();
    }
}
