using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class BsftHttpEgressOptionsValidatorTests
{
    private readonly IValidateOptions<BsftHttpEgressOptions> _validator = new BsftHttpEgressOptionsValidator();

    [Fact]
    public void Validate_TheDefaults_Pass() =>
        _validator.Validate(name: null, new BsftHttpEgressOptions()).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_ABareHostName_Passes()
    {
        var options = new BsftHttpEgressOptions();
        options.AllowedHosts.Add("relay.internal");

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ABlankAllowListEntry_Fails(string host)
    {
        var options = new BsftHttpEgressOptions();
        options.AllowedHosts.Add(host);

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("blank");
    }

    [Theory]
    [InlineData("https://relay.internal")]
    [InlineData("relay.internal/send")]
    [InlineData("relay.internal:8443")]
    public void Validate_AnAllowListEntryThatIsNotABareHost_Fails(string host)
    {
        // Such an entry matches nothing, so it reads as a hole that was opened while it never fires.
        var options = new BsftHttpEgressOptions();
        options.AllowedHosts.Add(host);

        var result = _validator.Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(host);
    }

    /// <summary>
    /// An IPv6 literal is all colons, and the colon check exists to catch a port. Refusing it would
    /// fail the host at startup over an entry that is exactly what the allow list is for.
    /// </summary>
    [Theory]
    [InlineData("::1")]
    [InlineData("fd12:3456::7")]
    [InlineData("10.1.2.3")]
    public void Validate_AnAddressLiteral_Passes(string host)
    {
        var options = new BsftHttpEgressOptions();
        options.AllowedHosts.Add(host);

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EveryComplaintIsCollected()
    {
        var options = new BsftHttpEgressOptions();
        options.AllowedHosts.Add("");
        options.AllowedHosts.Add("https://relay.internal");

        _validator.Validate(name: null, options).Failures.Should().HaveCount(2);
    }

    [Fact]
    public void Validate_WithoutOptions_Throws()
    {
        var act = () => _validator.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
