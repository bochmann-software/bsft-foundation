using FluentAssertions;
using Xunit;

namespace Bsft.Outbox.Abstractions.Tests;

/// <summary>Tests what a drainer written before the start-gated overload keeps doing.</summary>
public class OutboxDrainerContractTests
{
    [Fact]
    public async Task DrainOnceAsync_OnAnImplementationWithoutTheOverload_FallsBackToThePlainPass()
    {
        var drainer = new PlainDrainer();
        using var source = new CancellationTokenSource();

        var applied = await ((IOutboxDrainer<string>)drainer).DrainOnceAsync(() => false, source.Token);

        applied.Should().Be(3);
        drainer.Token.Should().Be(source.Token);
    }

    private sealed class PlainDrainer : IOutboxDrainer<string>
    {
        public CancellationToken Token { get; private set; }

        public Task<int> DrainOnceAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;

            return Task.FromResult(3);
        }
    }
}
