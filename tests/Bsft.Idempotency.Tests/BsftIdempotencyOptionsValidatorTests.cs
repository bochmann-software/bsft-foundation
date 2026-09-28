using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class BsftIdempotencyOptionsValidatorTests
{
    [Fact]
    public void Validate_TheDefaults_Succeed()
    {
        var result = Validate(new BsftIdempotencyOptions());

        result.Succeeded.Should().BeTrue();
        result.Failures.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_ABlankHeaderName_Fails(string? headerName)
    {
        var result = Validate(new BsftIdempotencyOptions { HeaderName = headerName! });

        result.Failures.Should().ContainSingle().Which.Should().Contain("HeaderName");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(IdempotencyDefaults.MaxSupportedKeyLength + 1)]
    public void Validate_AKeyLengthOutsideWhatTheStoresAccept_Fails(int maxKeyLength)
    {
        var result = Validate(new BsftIdempotencyOptions { MaxKeyLength = maxKeyLength });

        result.Failures.Should().ContainSingle().Which.Should().Contain("MaxKeyLength");
    }

    [Fact]
    public void Validate_TheWidestSupportedKeyLength_Succeeds()
    {
        var result = Validate(
            new BsftIdempotencyOptions { MaxKeyLength = IdempotencyDefaults.MaxSupportedKeyLength });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ABodyBoundOfZero_Fails()
    {
        var result = Validate(new BsftIdempotencyOptions { MaxBodyBytes = 0 });

        result.Failures.Should().ContainSingle().Which.Should().Contain("MaxBodyBytes");
    }

    [Fact]
    public void Validate_AResponseBoundOfZero_Fails()
    {
        var result = Validate(new BsftIdempotencyOptions { MaxResponseBytes = 0 });

        result.Failures.Should().ContainSingle().Which.Should().Contain("MaxResponseBytes");
    }

    [Fact]
    public void Validate_ARetentionOfZero_Fails()
    {
        var result = Validate(new BsftIdempotencyOptions { Retention = TimeSpan.Zero });

        result.Failures.Should().ContainSingle().Which.Should().Contain("Retention must be positive");
    }

    [Fact]
    public void Validate_ANegativeInProgressTimeout_Fails()
    {
        var result = Validate(
            new BsftIdempotencyOptions { InProgressTimeout = TimeSpan.FromSeconds(-1) });

        result.Failures.Should().ContainSingle().Which.Should().Contain("InProgressTimeout must be positive");
    }

    [Fact]
    public void Validate_ARetryAfterOfZero_Fails()
    {
        var result = Validate(new BsftIdempotencyOptions { InProgressRetryAfter = TimeSpan.Zero });

        result.Failures.Should().ContainSingle().Which.Should().Contain("InProgressRetryAfter");
    }

    [Fact]
    public void Validate_ALeaseOutlivingTheReplayWindow_Fails()
    {
        var result = Validate(new BsftIdempotencyOptions
        {
            Retention = TimeSpan.FromSeconds(30),
            InProgressTimeout = TimeSpan.FromSeconds(31),
        });

        result.Failures.Should().ContainSingle().Which.Should().Contain("must not exceed");
    }

    [Fact]
    public void Validate_ALeaseEqualToTheReplayWindow_Succeeds()
    {
        var result = Validate(new BsftIdempotencyOptions
        {
            Retention = TimeSpan.FromSeconds(30),
            InProgressTimeout = TimeSpan.FromSeconds(30),
        });

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_ANonPositiveRetention_IsNotAlsoReportedAsOutlivingTheWindow()
    {
        var result = Validate(new BsftIdempotencyOptions
        {
            Retention = TimeSpan.Zero,
            InProgressTimeout = TimeSpan.FromSeconds(31),
        });

        result.Failures.Should().ContainSingle().Which.Should().Contain("Retention must be positive");
    }

    [Fact]
    public void Validate_ANonPositiveLease_IsNotAlsoReportedAsOutlivingTheWindow()
    {
        var result = Validate(new BsftIdempotencyOptions
        {
            Retention = TimeSpan.FromSeconds(30),
            InProgressTimeout = TimeSpan.Zero,
        });

        result.Failures.Should().ContainSingle().Which.Should().Contain("InProgressTimeout must be positive");
    }

    [Fact]
    public void Validate_NoMethodAtAll_Fails()
    {
        var options = new BsftIdempotencyOptions();

        options.Methods.Clear();

        Validate(options).Failures.Should().ContainSingle().Which.Should().Contain("at least one method");
    }

    [Fact]
    public void Validate_ABlankMethod_Fails()
    {
        var options = new BsftIdempotencyOptions();

        options.Methods.Add("  ");

        Validate(options).Failures.Should().ContainSingle().Which.Should().Contain("blank method");
    }

    [Fact]
    public void Validate_ABlankCachedHeaderName_Fails()
    {
        var options = new BsftIdempotencyOptions();

        options.CachedResponseHeaders.Add("");

        Validate(options).Failures.Should().ContainSingle().Which.Should().Contain("blank header name");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Validate_AStatusThatIsNotAStatus_Fails(int statusCode)
    {
        var options = new BsftIdempotencyOptions();

        options.NonReplayableStatusCodes.Add(statusCode);

        Validate(options).Failures.Should().ContainSingle().Which.Should().Contain("NonReplayableStatusCodes");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(599)]
    public void Validate_AStatusAtTheEdgeOfTheRange_Succeeds(int statusCode)
    {
        var options = new BsftIdempotencyOptions();

        options.NonReplayableStatusCodes.Add(statusCode);

        Validate(options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_EveryFailureAtOnce_ReportsThemAll()
    {
        var options = new BsftIdempotencyOptions
        {
            HeaderName = " ",
            MaxKeyLength = 0,
            MaxBodyBytes = 0,
            Retention = TimeSpan.Zero,
            InProgressTimeout = TimeSpan.Zero,
            InProgressRetryAfter = TimeSpan.Zero,
        };

        options.Methods.Clear();

        Validate(options).Failures.Should().HaveCount(7);
    }

    [Fact]
    public void Validate_NullOptions_IsRefused() =>
        FluentActions.Invoking(() => new BsftIdempotencyOptionsValidator().Validate(null, null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("options");

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(BsftIdempotencyOptions options) =>
        new BsftIdempotencyOptionsValidator().Validate(Microsoft.Extensions.Options.Options.DefaultName, options);
}
