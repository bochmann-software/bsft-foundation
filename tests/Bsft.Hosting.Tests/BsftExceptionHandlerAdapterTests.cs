using Bsft.Core;
using Bsft.Correlation;
using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Tests.Fakes;
using Bsft.Hosting.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Text.RegularExpressions;
using Xunit;

namespace Bsft.Hosting.Tests;

public class BsftExceptionHandlerAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TryHandleAsync_SameFailure_WritesTheSameBodyAsTheMiddleware(bool withProblemDetails)
    {
        void Configure(IServiceCollection services)
        {
            services.AddBsftExceptionHandler();
            if (withProblemDetails)
                services.AddProblemDetails();
        }

        Task Fail(HttpContext _) => throw new NotFoundException("Order", 7);

        using var middlewareHost = await ExceptionHost.StartAsync(false, Fail, Configure);
        using var handlerHost = await ExceptionHost.StartAsync(true, Fail, Configure);

        var viaMiddleware = await middlewareHost.GetTestClient().GetAsync("/");
        var viaHandler = await handlerHost.GetTestClient().GetAsync("/");

        viaHandler.StatusCode.Should().Be(viaMiddleware.StatusCode);
        // ProblemDetails defaults add the per-request trace id, which differs between two requests by design.
        WithoutTraceId(await viaHandler.Content.ReadAsStringAsync()).Should().Be(WithoutTraceId(await viaMiddleware.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledException_WritesTheFixedBodyWithoutTheExceptionText()
    {
        using var host = await ExceptionHost.StartAsync(
            true, _ => throw new InvalidOperationException("secret"), services => services.AddBsftExceptionHandler());

        var body = await (await host.GetTestClient().GetAsync("/")).Content.ReadAsStringAsync();

        body.Should().NotContain("secret").And.Contain("An unexpected error occurred.");
    }

    [Fact]
    public async Task TryHandleAsync_CancellationOfAnAbortedRequest_IsNotHandled()
    {
        var handler = CreateHandler();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = new DefaultHttpContext { RequestAborted = cts.Token };

        var handled = await handler.TryHandleAsync(context, new OperationCanceledException(cts.Token), CancellationToken.None);

        handled.Should().BeFalse();
    }

    [Fact]
    public async Task TryHandleAsync_NullHttpContext_Throws()
    {
        var act = async () => await CreateHandler().TryHandleAsync(null!, new InvalidOperationException(), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task TryHandleAsync_NullException_Throws()
    {
        var act = async () => await CreateHandler().TryHandleAsync(new DefaultHttpContext(), null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullArgument_Throws()
    {
        var responder = Substitute.For<IBsftExceptionResponder>();
        var logger = NullLogger<BsftExceptionHandlerAdapter>.Instance;

        ((Action)(() => new BsftExceptionHandlerAdapter(null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new BsftExceptionHandlerAdapter(responder, null!))).Should().Throw<ArgumentNullException>();
    }

    private static string WithoutTraceId(string body) => Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"-\"");

    private static BsftExceptionHandlerAdapter CreateHandler() =>
        new(Substitute.For<IBsftExceptionResponder>(), NullLogger<BsftExceptionHandlerAdapter>.Instance);
}
