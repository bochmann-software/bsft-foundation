using Bsft.Core;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Bsft.Mediator.Tests;

public class ValidationBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithNoValidators_CallsNext()
    {
        var validators = Enumerable.Empty<IValidator<TestCommand>>();
        var sut = new ValidationBehavior<TestCommand, string>(validators);
        var nextCalled = false;

        var result = await sut.HandleAsync(
            new TestCommand("test"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult("ok");
            });

        result.Should().Be("ok");
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithPassingValidator_CallsNext()
    {
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Success());

        var sut = new ValidationBehavior<TestCommand, string>([validator]);

        var result = await sut.HandleAsync(
            new TestCommand("test"),
            () => Task.FromResult("ok"));

        result.Should().Be("ok");
        await validator.Received(1).ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithFailingValidator_ThrowsValidationException()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Name is required"]
        };
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Failure(errors));

        var sut = new ValidationBehavior<TestCommand, string>([validator]);

        var act = () => sut.HandleAsync(
            new TestCommand("test"),
            () => Task.FromResult("should not reach"));

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().ContainKey("Name");
        exception.Which.Errors["Name"].Should().Contain("Name is required");
    }

    [Fact]
    public async Task HandleAsync_WithMultipleValidators_AggregatesErrors()
    {
        var validator1 = Substitute.For<IValidator<TestCommand>>();
        validator1.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Failure(new Dictionary<string, string[]>
            {
                ["Name"] = ["Name is required"]
            }));

        var validator2 = Substitute.For<IValidator<TestCommand>>();
        validator2.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(ValidationResult.Failure(new Dictionary<string, string[]>
            {
                ["Email"] = ["Email is invalid"]
            }));

        var sut = new ValidationBehavior<TestCommand, string>([validator1, validator2]);

        var act = () => sut.HandleAsync(
            new TestCommand("test"),
            () => Task.FromResult("should not reach"));

        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Errors.Should().HaveCount(2);
        exception.Which.Errors.Should().ContainKey("Name");
        exception.Which.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public void Constructor_WithNullValidators_ThrowsArgumentNullException()
    {
        var act = () => new ValidationBehavior<TestCommand, string>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("validators");
    }
}
