using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests;

public class BsftIdempotencyOptionsTests
{
    [Fact]
    public void TheDefaults_AreTheOnesTheDocumentationPromises()
    {
        var options = new BsftIdempotencyOptions();

        options.HeaderName.Should().Be(IdempotencyDefaults.HeaderName);
        options.Methods.Should().ContainSingle().Which.Should().Be("POST");
        options.ShouldRequireKey.Should().BeFalse();
        options.Retention.Should().Be(TimeSpan.FromHours(24));
        options.InProgressTimeout.Should().Be(TimeSpan.FromSeconds(60));
        options.InProgressRetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        options.MaxBodyBytes.Should().Be(1024 * 1024);
        options.MaxKeyLength.Should().Be(255);
        options.CachedResponseHeaders.Should().Equal("Location", "Content-Type");
        options.NonReplayableStatusCodes.Should().Equal(401, 403, 408, 429);
        options.Mode.Should().Be(IdempotencyMode.Detached);
    }

    [Fact]
    public void TheSectionName_IsTheFrameworkPrefixedOne() =>
        BsftIdempotencyOptions.SectionName.Should().Be("Bsft:Idempotency");

    [Fact]
    public void EveryMutableSetting_KeepsWhatItIsGiven()
    {
        var options = new BsftIdempotencyOptions
        {
            HeaderName = "X-Key",
            ShouldRequireKey = true,
            Retention = TimeSpan.FromMinutes(5),
            InProgressTimeout = TimeSpan.FromMinutes(1),
            InProgressRetryAfter = TimeSpan.FromSeconds(3),
            MaxBodyBytes = 64,
            MaxKeyLength = 32,
            Mode = IdempotencyMode.Enlisted,
        };

        options.HeaderName.Should().Be("X-Key");
        options.ShouldRequireKey.Should().BeTrue();
        options.Retention.Should().Be(TimeSpan.FromMinutes(5));
        options.InProgressTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.InProgressRetryAfter.Should().Be(TimeSpan.FromSeconds(3));
        options.MaxBodyBytes.Should().Be(64);
        options.MaxKeyLength.Should().Be(32);
        options.Mode.Should().Be(IdempotencyMode.Enlisted);
    }
}
