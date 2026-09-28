using Bsft.Core;
using FluentAssertions;
using Xunit;

namespace Bsft.Resilience.Mediator.Tests;

public class BsftResilienceMediatorOptionsTests
{
    [Fact]
    public void SectionName_IsTheDocumentedSection() =>
        BsftResilienceMediatorOptions.SectionName.Should().Be("Bsft:Resilience:Mediator");

    [Fact]
    public void Defaults_AreSet()
    {
        var options = new BsftResilienceMediatorOptions();

        Func<Exception?, bool> expectedDefault = BsftResilienceMediatorOptions.IsTransientFailure;

        options.MaxRetryAttempts.Should().Be(3);
        options.BaseDelay.Should().Be(TimeSpan.FromMilliseconds(200));
        options.ShouldRetry.Should().Be(expectedDefault);
    }

    [Fact]
    public void IsTransientFailure_DoesNotRetryNullCancellationOrDomainExceptions()
    {
        BsftResilienceMediatorOptions.IsTransientFailure(null).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new OperationCanceledException()).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new ValidationException(new Dictionary<string, string[]>())).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new BusinessRuleException("rule")).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new NotFoundException("Entity", 1)).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new ForbiddenException("denied")).Should().BeFalse();
        BsftResilienceMediatorOptions.IsTransientFailure(new ConflictException("conflict")).Should().BeFalse();
    }

    [Fact]
    public void IsTransientFailure_RetriesTransientExceptions()
    {
        BsftResilienceMediatorOptions.IsTransientFailure(new InvalidOperationException()).Should().BeTrue();
        BsftResilienceMediatorOptions.IsTransientFailure(new TimeoutException()).Should().BeTrue();
    }
}
