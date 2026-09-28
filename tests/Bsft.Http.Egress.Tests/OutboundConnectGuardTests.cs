using System.Net.Sockets;
using System.Net;
using Bsft.Http.Egress.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Http.Egress.Tests;

public class OutboundConnectGuardTests
{
    private static readonly IPAddress Public = IPAddress.Parse("93.184.216.34");
    private static readonly IPAddress Private = IPAddress.Parse("10.0.0.7");

    [Fact]
    public async Task SelectAddressAsync_APublicAddress_IsUsed()
    {
        var guard = Guard(new StubEgressAddressResolver([Public]));

        (await guard.SelectAddressAsync("hooks.example.com")).Should().Be(Public);
    }

    [Fact]
    public async Task SelectAddressAsync_SkipsTheAddressesItMayNotCall()
    {
        var guard = Guard(new StubEgressAddressResolver([Private, Public]));

        (await guard.SelectAddressAsync("hooks.example.com")).Should().Be(Public);
    }

    [Fact]
    public async Task SelectAddressAsync_OnlyForbiddenAddresses_Refuses()
    {
        var guard = Guard(new StubEgressAddressResolver([Private]));

        var act = () => guard.SelectAddressAsync("hooks.example.com");

        await act.Should().ThrowAsync<OutboundAddressRejectedException>();
    }

    [Fact]
    public async Task SelectAddressAsync_AHostThatResolvesToNothing_Refuses()
    {
        var guard = Guard(new StubEgressAddressResolver([]));

        var act = () => guard.SelectAddressAsync("hooks.example.com");

        await act.Should().ThrowAsync<OutboundAddressRejectedException>();
    }

    [Fact]
    public async Task SelectAddressAsync_ARejectionNeverNamesTheAddress()
    {
        var guard = Guard(new StubEgressAddressResolver([Private]));

        var thrown = await Assert.ThrowsAsync<OutboundAddressRejectedException>(
            () => guard.SelectAddressAsync("hooks.example.com"));

        thrown.Message.Should().NotContain("10.0.0.7");
    }

    [Fact]
    public async Task SelectAddressAsync_ResolvesAfreshRatherThanTrustingTheEarlierAnswer()
    {
        // The rebinding case: the name answered with a public address while the destination was being
        // validated, and answers with an internal one now. The connect layer is what catches it.
        var resolver = new StubEgressAddressResolver([Public], [Private]);
        var guard = Guard(resolver);

        (await guard.SelectAddressAsync("hooks.example.com")).Should().Be(Public);

        var act = () => guard.SelectAddressAsync("hooks.example.com");

        await act.Should().ThrowAsync<OutboundAddressRejectedException>();
        resolver.Calls.Should().Be(2);
    }

    [Fact]
    public async Task SelectAddressAsync_UnderThePrivateNetworkOverride_AllowsAnInternalAddress()
    {
        var guard = Guard(new StubEgressAddressResolver([Private]), options => options.AllowPrivateNetworks = true);

        (await guard.SelectAddressAsync("localhost")).Should().Be(Private);
    }

    [Fact]
    public async Task SelectAddressAsync_AnAllowListedHost_IsCalledAtWhateverItResolvesTo()
    {
        var guard = Guard(
            new StubEgressAddressResolver([Private]), options => options.AllowedHosts.Add("relay.internal"));

        (await guard.SelectAddressAsync("relay.internal")).Should().Be(Private);
    }

    [Fact]
    public async Task ConnectAsync_AnAllowedAddress_Connects()
    {
        using var listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        var port = ((IPEndPoint)listener.LocalEndPoint!).Port;

        var guard = Guard(
            new StubEgressAddressResolver([IPAddress.Loopback]), options => options.AllowPrivateNetworks = true);

        await using var stream = await guard.ConnectAsync("localhost", port);

        stream.Should().BeOfType<NetworkStream>();
    }

    [Fact]
    public async Task ConnectAsync_AnAddressItMayNotCall_NeverOpensASocket()
    {
        var guard = Guard(new StubEgressAddressResolver([Private]));

        var act = () => guard.ConnectAsync("hooks.example.com", 443);

        await act.Should().ThrowAsync<OutboundAddressRejectedException>();
    }

    [Fact]
    public async Task ConnectAsync_ARefusedConnection_Surfaces()
    {
        // A port nothing is listening on: bound and closed again, so the port is almost certainly free.
        using var probe = new Socket(SocketType.Stream, ProtocolType.Tcp);
        probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)probe.LocalEndPoint!).Port;
        probe.Close();

        var guard = Guard(
            new StubEgressAddressResolver([IPAddress.Loopback]), options => options.AllowPrivateNetworks = true);

        var act = () => guard.ConnectAsync("localhost", port);

        await act.Should().ThrowAsync<SocketException>();
    }

    [Fact]
    public void Constructor_RejectsMissingCollaborators()
    {
        var withoutPolicy = () => new OutboundConnectGuard(null!, new StubEgressAddressResolver());
        var withoutResolver = () => new OutboundConnectGuard(Policy(), null!);

        withoutPolicy.Should().Throw<ArgumentNullException>();
        withoutResolver.Should().Throw<ArgumentNullException>();
    }

    private static IOutboundConnectGuard Guard(
        IEgressAddressResolver resolver, Action<BsftHttpEgressOptions>? configure = null)
        => new OutboundConnectGuard(Policy(configure), resolver);

    private static IOutboundAddressPolicy Policy(Action<BsftHttpEgressOptions>? configure = null)
    {
        var options = new BsftHttpEgressOptions();
        configure?.Invoke(options);

        return new OutboundAddressPolicy(Microsoft.Extensions.Options.Options.Create(options));
    }
}
