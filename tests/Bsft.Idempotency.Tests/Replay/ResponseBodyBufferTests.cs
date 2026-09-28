using Bsft.Idempotency.Replay;
using Bsft.Idempotency.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using System.Text;
using Xunit;

namespace Bsft.Idempotency.Tests.Replay;

public class ResponseBodyBufferTests
{
    [Fact]
    public void Constructor_DivertsTheResponseIntoMemory()
    {
        var context = HttpContexts.Create();
        var original = context.Response.Body;

        using var buffer = new ResponseBodyBuffer(context.Response);

        context.Response.Body.Should().NotBeSameAs(original);
    }

    [Fact]
    public async Task Capture_TakesTheStatusTheAllowlistedHeadersAndTheBytes()
    {
        var context = HttpContexts.Create();
        var original = (MemoryStream)context.Response.Body;

        using var buffer = new ResponseBodyBuffer(context.Response);

        context.Response.StatusCode = 201;
        context.Response.Headers.Location = "/orders/1";
        context.Response.ContentType = "application/json";
        context.Response.Headers.SetCookie = "session=secret";

        await context.Response.WriteAsync("{\"id\":1}");

        var snapshot = buffer.Capture(["Location", "Content-Type"]);

        snapshot.StatusCode.Should().Be(201);
        snapshot.Headers.Should().ContainKey("Location").WhoseValue.Should().Be("/orders/1");
        snapshot.Headers.Should().ContainKey("Content-Type");
        snapshot.Headers.Should().NotContainKey("Set-Cookie");
        Encoding.UTF8.GetString(snapshot.Body).Should().Be("{\"id\":1}");
        original.Length.Should().Be(0);
    }

    [Fact]
    public void Capture_AHeaderTheResponseNeverSet_IsNotStored()
    {
        var context = HttpContexts.Create();

        using var buffer = new ResponseBodyBuffer(context.Response);

        buffer.Capture(["Location"]).Headers.Should().BeEmpty();
    }

    [Fact]
    public void Capture_AHeaderPresentButEmpty_IsNotStored()
    {
        var context = HttpContexts.Create();

        using var buffer = new ResponseBodyBuffer(context.Response);

        context.Response.Headers["Location"] = string.Empty;

        buffer.Capture(["Location"]).Headers.Should().BeEmpty();
    }

    [Fact]
    public void Capture_TheAllowlistIsMatchedWithoutRegardToCase()
    {
        var context = HttpContexts.Create();

        using var buffer = new ResponseBodyBuffer(context.Response);

        context.Response.Headers.Location = "/orders/1";

        buffer.Capture(["location"]).Headers.Should().ContainKey("Location");
    }

    [Fact]
    public async Task ReleaseAsync_PutsTheRealBodyBackAndWritesWhatWasBuffered()
    {
        var context = HttpContexts.Create();
        var original = (MemoryStream)context.Response.Body;

        using (var buffer = new ResponseBodyBuffer(context.Response))
        {
            await context.Response.WriteAsync("{\"id\":1}");
            await buffer.ReleaseAsync();
        }

        context.Response.Body.Should().BeSameAs(original);
        Encoding.UTF8.GetString(original.ToArray()).Should().Be("{\"id\":1}");
    }

    [Fact]
    public void Dispose_PutsTheRealBodyBackSoAThrownRequestLeavesThePipelineAsItFoundIt()
    {
        var context = HttpContexts.Create();
        var original = context.Response.Body;

        new ResponseBodyBuffer(context.Response).Dispose();

        context.Response.Body.Should().BeSameAs(original);
    }

    [Fact]
    public void Constructor_NullResponse_IsRefused() =>
        FluentActions.Invoking(() => new ResponseBodyBuffer(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("response");

    [Fact]
    public void Capture_NullAllowlist_IsRefused()
    {
        using var buffer = new ResponseBodyBuffer(HttpContexts.Create().Response);

        FluentActions.Invoking(() => buffer.Capture(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("allowedHeaders");
    }
}
