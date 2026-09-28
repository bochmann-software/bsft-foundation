using Bsft.Core;
using Bsft.Idempotency.Replay;
using FluentAssertions;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class IdempotencyKeyGuardTests
{
    [Theory]
    [InlineData("0f9f1d3e-3c7f-4a1e-9f2a-7c5f1e2d3b4c")]
    [InlineData("01HQ8XW3ZK9V2TQ7M4N6P8R0S1")]
    [InlineData("order:2026-08-17.retry_1")]
    [InlineData("a")]
    public void Ensure_AKeyOfTheShapeEveryRealClientMints_IsAccepted(string key) =>
        FluentActions.Invoking(() => IdempotencyKeyGuard.Ensure(key, 255)).Should().NotThrow();

    [Fact]
    public void Ensure_AKeyExactlyAtTheBound_IsAccepted() =>
        FluentActions.Invoking(() => IdempotencyKeyGuard.Ensure(new string('a', 16), 16))
            .Should().NotThrow();

    [Fact]
    public void Ensure_AKeyPastTheBound_IsRefusedAsAValidationErrorOnTheHeader()
    {
        var thrown = FluentActions.Invoking(() => IdempotencyKeyGuard.Ensure(new string('a', 17), 16))
            .Should().Throw<ValidationException>().Which;

        thrown.Errors.Should().ContainKey(IdempotencyDefaults.KeyValidationField);
        thrown.Errors[IdempotencyDefaults.KeyValidationField].Should().ContainSingle()
            .Which.Should().Contain("at most 16 characters");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has\nnewline")]
    [InlineData("naïve")]
    [InlineData("<script>")]
    public void Ensure_AKeyCarryingACharacterOutsideTheAcceptedSet_IsRefused(string key)
    {
        var thrown = FluentActions.Invoking(() => IdempotencyKeyGuard.Ensure(key, 255))
            .Should().Throw<ValidationException>().Which;

        thrown.Errors[IdempotencyDefaults.KeyValidationField].Should().ContainSingle()
            .Which.Should().Contain("letters, digits");
    }
}
