using Bsft.Data.MultiTenancy.Sandboxes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy.Sandboxes;

/// <summary>
/// Handing out an isolated tenant and taking it away: the identifier nothing else has, and a teardown
/// that only touches one this seam gave out.
/// </summary>
public sealed class SandboxTenantProvisionerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ISandboxTenantRegistry _registry = Substitute.For<ISandboxTenantRegistry>();

    [Fact]
    public async Task ProvisionAsync_AUsableRequest_ClaimsAnIdentifierAndDescribesIt()
    {
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(true);

        var sandbox = await Provisioner().ProvisionAsync(new SandboxTenantSpecification
        {
            Label = "Trying the reports API",
            RequestedBy = "dev-42",
            Lifetime = TimeSpan.FromDays(2),
        });

        sandbox.TenantId.Should().StartWith("sandbox-");
        sandbox.Label.Should().Be("Trying the reports API");
        sandbox.RequestedBy.Should().Be("dev-42");
        sandbox.CreatedAt.Should().Be(Now);
        sandbox.ExpiresAt.Should().Be(Now.AddDays(2));

        await _registry.Received(1).TryReserveAsync(
            Arg.Is<SandboxTenant>(claimed => claimed.TenantId == sandbox.TenantId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionAsync_WithoutALifetime_UsesTheConfiguredOne()
    {
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(true);

        var sandbox = await Provisioner(options => options.DefaultLifetime = TimeSpan.FromHours(6))
            .ProvisionAsync(Spec());

        sandbox.ExpiresAt.Should().Be(Now.AddHours(6));
    }

    [Fact]
    public async Task ProvisionAsync_TwoRequests_GetTwoIdentifiers()
    {
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(true);

        var provisioner = Provisioner();

        var first = await provisioner.ProvisionAsync(Spec());
        var second = await provisioner.ProvisionAsync(Spec());

        first.TenantId.Should().NotBe(second.TenantId);
    }

    [Fact]
    public async Task ProvisionAsync_AnIdentifierSomethingElseAlreadyHas_TriesAnother()
    {
        // The registry is the only place two callers racing for one identifier can be told apart, so a
        // refused claim is an answer to act on rather than a failure.
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(false, true);

        var sandbox = await Provisioner().ProvisionAsync(Spec());

        sandbox.TenantId.Should().NotBeEmpty();
        await _registry.ReceivedWithAnyArgs(2).TryReserveAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionAsync_ARegistryThatRefusesEveryClaim_FailsRatherThanSpins()
    {
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(false);

        var act = async () => await Provisioner(options => options.MaxProvisionAttempts = 3).ProvisionAsync(Spec());

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*3 attempts*");

        await _registry.ReceivedWithAnyArgs(3).TryReserveAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionAsync_TheConfiguredPrefix_IsWhatMakesASandboxObvious()
    {
        _registry.TryReserveAsync(Arg.Any<SandboxTenant>()).ReturnsForAnyArgs(true);

        var sandbox = await Provisioner(options => options.IdPrefix = "try").ProvisionAsync(Spec());

        sandbox.TenantId.Should().StartWith("try-");
    }

    [Theory]
    [InlineData("", "dev-42", null)]
    [InlineData("  ", "dev-42", null)]
    [InlineData("Trying things", "", null)]
    [InlineData("Trying things", "   ", null)]
    public async Task ProvisionAsync_ARequestThatSaysTooLittle_IsRefused(string label, string by, string? unused)
    {
        _ = unused;

        var act = async () => await Provisioner()
            .ProvisionAsync(new SandboxTenantSpecification { Label = label, RequestedBy = by });

        await act.Should().ThrowAsync<InvalidOperationException>();

        await _registry.DidNotReceiveWithAnyArgs().TryReserveAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionAsync_ALifetimeThatHasAlreadyExpired_IsRefused()
    {
        var act = async () => await Provisioner()
            .ProvisionAsync(new SandboxTenantSpecification { Label = "x", RequestedBy = "y", Lifetime = TimeSpan.Zero });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public async Task ProvisionAsync_WithoutASpec_Refuses()
    {
        var act = async () => await Provisioner().ProvisionAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("spec");
    }

    [Fact]
    public async Task TearDownAsync_ASandboxThisSeamHandedOut_ClearsEveryHandlerThenForgetsIt()
    {
        var first = Substitute.For<ISandboxTenantCleanup>();
        var second = Substitute.For<ISandboxTenantCleanup>();

        _registry.FindAsync("sandbox-1").ReturnsForAnyArgs(Sandbox("sandbox-1"));

        var teardown = await Provisioner(cleanups: [first, second]).TearDownAsync("sandbox-1");

        teardown.TenantId.Should().Be("sandbox-1");
        teardown.WasKnown.Should().BeTrue();
        teardown.ClearedBy.Should().HaveCount(2);

        await first.Received(1).ClearAsync("sandbox-1", Arg.Any<CancellationToken>());
        await second.Received(1).ClearAsync("sandbox-1", Arg.Any<CancellationToken>());
        await _registry.Received(1).RemoveAsync("sandbox-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TearDownAsync_ASandboxThisSeamHandedOut_NamesEachHandlerInFull()
    {
        // Two handlers of one simple name in different namespaces are two handlers.
        _registry.FindAsync("sandbox-1").ReturnsForAnyArgs(Sandbox("sandbox-1"));

        var teardown = await Provisioner(cleanups: [new NamedCleanup()]).TearDownAsync("sandbox-1");

        teardown.ClearedBy.Should().Equal(typeof(NamedCleanup).FullName);
    }

    [Fact]
    public async Task TearDownAsync_AnIdentifierTheRegistryDoesNotKnow_TouchesNothing()
    {
        // Otherwise naming a real tenant is a way to have its rows deleted, and this call is reachable
        // from wherever a host puts it.
        var cleanup = Substitute.For<ISandboxTenantCleanup>();

        _registry.FindAsync("acme").ReturnsForAnyArgs((SandboxTenant?)null);

        var teardown = await Provisioner(cleanups: [cleanup]).TearDownAsync("acme");

        teardown.WasKnown.Should().BeFalse();
        teardown.ClearedBy.Should().BeEmpty();

        await cleanup.DidNotReceiveWithAnyArgs().ClearAsync(default!, default);
        await _registry.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task TearDownAsync_AHandlerThatFails_LeavesTheSandboxListedSoItCanBeTornDownAgain()
    {
        // A half-cleared tenant that is still listed is recoverable; one forgotten while its rows
        // survive is a leak nobody can name.
        var cleanup = Substitute.For<ISandboxTenantCleanup>();

        cleanup.ClearAsync(default!, default).ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("no")));
        _registry.FindAsync("sandbox-1").ReturnsForAnyArgs(Sandbox("sandbox-1"));

        var act = async () => await Provisioner(cleanups: [cleanup]).TearDownAsync("sandbox-1");

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _registry.DidNotReceiveWithAnyArgs().RemoveAsync(default!, default);
    }

    [Fact]
    public async Task TearDownAsync_WithNoHandlersRegistered_StillForgetsItAndSaysWhatItCleared()
    {
        _registry.FindAsync("sandbox-1").ReturnsForAnyArgs(Sandbox("sandbox-1"));

        var teardown = await Provisioner().TearDownAsync("sandbox-1");

        teardown.WasKnown.Should().BeTrue();
        teardown.ClearedBy.Should().BeEmpty();

        await _registry.Received(1).RemoveAsync("sandbox-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TearDownAsync_WithoutAnIdentifier_Refuses()
    {
        var act = async () => await Provisioner().TearDownAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Ctor_WithoutOneOfItsCollaborators_Refuses()
    {
        var options = Options();
        var clock = new FixedClock(Now);

        var noRegistry = () => new SandboxTenantProvisioner(null!, [], options, clock);
        var noCleanups = () => new SandboxTenantProvisioner(_registry, null!, options, clock);
        var noOptions = () => new SandboxTenantProvisioner(_registry, [], null!, clock);
        var noClock = () => new SandboxTenantProvisioner(_registry, [], options, null!);

        noRegistry.Should().Throw<ArgumentNullException>().WithParameterName("registry");
        noCleanups.Should().Throw<ArgumentNullException>().WithParameterName("cleanups");
        noOptions.Should().Throw<ArgumentNullException>().WithParameterName("options");
        noClock.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }

    private static SandboxTenantSpecification Spec() => new() { Label = "Trying things", RequestedBy = "dev-42" };

    private static SandboxTenant Sandbox(string tenantId) => new()
    {
        TenantId = tenantId,
        Label = "Trying things",
        RequestedBy = "dev-42",
        CreatedAt = Now,
    };

    private static IOptionsMonitor<BsftSandboxTenantOptions> Options(Action<BsftSandboxTenantOptions>? configure = null)
    {
        var options = new BsftSandboxTenantOptions();

        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<BsftSandboxTenantOptions>>();

        monitor.CurrentValue.Returns(options);

        return monitor;
    }

    private ISandboxTenantProvisioner Provisioner(
        Action<BsftSandboxTenantOptions>? configure = null, IReadOnlyList<ISandboxTenantCleanup>? cleanups = null)
        => new SandboxTenantProvisioner(_registry, cleanups ?? [], Options(configure), new FixedClock(Now));
}
