using Bsft.Core.Tenancy;
using Bsft.Idempotency.EFCore;
using Bsft.Idempotency.EFCore.Tests.Fakes;
using Bsft.Idempotency.EFCore.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Tests.Shared;

namespace Bsft.Idempotency.EFCore.Tests;

public class IdempotencyRecordSweeperTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);
    private readonly ManualTimeProvider _clock = new(Now);

    private IdempotencyRecordSweeper CreateSweeper(
        IServiceScopeFactory? scopeFactory = null,
        BsftIdempotencyOptions? options = null)
    {
        var opts = options ?? new BsftIdempotencyOptions { Retention = TimeSpan.FromDays(30) };
        var factory = scopeFactory ?? new FakeServiceScopeFactory();

        return new IdempotencyRecordSweeper(
            factory,
            Options.Create(opts),
            _clock,
            NullLogger<IdempotencyRecordSweeper>.Instance);
    }

    private IdempotencyRecordSweeper CreateSweeperWithTenants(
        IBackgroundTenantSource tenants,
        IServiceScopeFactory? scopeFactory = null,
        BsftIdempotencyOptions? options = null)
    {
        var opts = options ?? new BsftIdempotencyOptions { Retention = TimeSpan.FromDays(30) };
        var factory = scopeFactory ?? new FakeServiceScopeFactory();

        return new IdempotencyRecordSweeper(
            factory,
            Options.Create(opts),
            _clock,
            NullLogger<IdempotencyRecordSweeper>.Instance,
            tenants);
    }

    [Fact]
    public async Task RunLoopAsync_WithATenantSource_SweepsOncePerTenant()
    {
        var scopes = new FakeServiceScopeFactory();
        var sweeper = CreateSweeperWithTenants(new FakeBackgroundTenantSource(["a", "b"]), scopes);

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, scopes);

        openedFor.Take(2).Should().Equal("a", "b");
    }

    [Fact]
    public async Task RunLoopAsync_WithoutATenantSource_SweepsWithoutATenant()
    {
        var scopes = new FakeServiceScopeFactory();
        var sweeper = CreateSweeper(scopes);

        var openedFor = await RunFirstPassAsync(sweeper.RunLoopAsync, scopes);

        openedFor[0].Should().BeNull();
    }

    private async Task<IReadOnlyList<string?>> RunFirstPassAsync(
        Func<CancellationToken, Task> run, FakeServiceScopeFactory scopes)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var loop = run(cts.Token);

        // The first pass is complete once the loop waits out its interval on the manual clock.
        await await Task.WhenAny(_clock.WaitForAnyTimerAsync(), loop);

        await cts.CancelAsync();
        await loop;

        return scopes.OpenedFor;
    }

    [Fact]
    public void Constructor_WithNullTenants_Throws()
    {
        var act = () => CreateSweeperWithTenants(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenants");
    }
}
