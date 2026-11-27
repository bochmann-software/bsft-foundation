using Bsft.Hosting.SecurityHeaders;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.Tests.SecurityHeaders;

public class BsftSecurityHeadersOptionsValidatorTests
{
    private readonly BsftSecurityHeadersOptionsValidator _validator = new();

    [Fact]
    public void Validate_WithDefaults_Succeeds() =>
        _validator.Validate(name: null, new BsftSecurityHeadersOptions()).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_WithEveryHeaderDisabled_Succeeds()
    {
        var options = new BsftSecurityHeadersOptions
        {
            ContentTypeOptions = null,
            FrameOptions = "",
            ReferrerPolicy = null,
            ContentSecurityPolicy = null,
            PermissionsPolicy = null,
        };

        _validator.Validate(name: null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithSameOriginFrameOptions_Succeeds() =>
        _validator.Validate(name: null, new BsftSecurityHeadersOptions { FrameOptions = "sameorigin" }).Succeeded.Should().BeTrue();

    [Fact]
    public void Validate_WithUnknownFrameOptions_Fails() =>
        _validator.Validate(name: null, new BsftSecurityHeadersOptions { FrameOptions = "ALLOW-FROM x" })
            .FailureMessage.Should().Contain("FrameOptions must be DENY or SAMEORIGIN");

    [Theory]
    [InlineData("ContentTypeOptions")]
    [InlineData("ReferrerPolicy")]
    [InlineData("ContentSecurityPolicy")]
    [InlineData("PermissionsPolicy")]
    public void Validate_WithLineBreakInValue_FailsNamingTheMember(string member)
    {
        var options = new BsftSecurityHeadersOptions();
        typeof(BsftSecurityHeadersOptions).GetProperty(member)!.SetValue(options, "a\r\nX-Injected: 1");

        _validator.Validate(name: null, options).FailureMessage.Should().Contain($"{member} must not contain control characters");
    }

    [Theory]
    [InlineData("openapi")]
    [InlineData("")]
    public void Validate_WithExemptPathWithoutLeadingSlash_Fails(string path)
    {
        var options = new BsftSecurityHeadersOptions();
        options.ContentSecurityPolicyExemptPaths.Add(path);

        _validator.Validate(name: null, options).FailureMessage.Should().Contain("must start with '/'");
    }

    [Fact]
    public void Validate_WithRootExemptPath_FailsPointingAtDisablingTheContentSecurityPolicy()
    {
        var options = new BsftSecurityHeadersOptions();
        options.ContentSecurityPolicyExemptPaths.Add("/");

        _validator.Validate(name: null, options).FailureMessage.Should().Contain("use ContentSecurityPolicy = null to disable the CSP");
    }

    [Fact]
    public void Validate_WithExemptPathContainingDoubleSlash_Fails()
    {
        var options = new BsftSecurityHeadersOptions();
        options.ContentSecurityPolicyExemptPaths.Add("/swagger//ui");

        _validator.Validate(name: null, options).FailureMessage.Should().Contain("must not contain '//'");
    }

    [Fact]
    public void Validate_WithNullOptions_Throws()
    {
        var act = () => _validator.Validate(name: null, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
