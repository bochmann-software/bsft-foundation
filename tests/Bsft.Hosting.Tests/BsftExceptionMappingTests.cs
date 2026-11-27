using Bsft.Core;
using Bsft.Core.Observers;
using Bsft.Hosting.DependencyInjection;
using Bsft.Hosting.Tests.Fakes;
using Bsft.Hosting.Tests.Support;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Bsft.Hosting.Tests;

/// <summary>The mapper and observer extension points, through both entry points.</summary>
public class BsftExceptionMappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_AppMapperMatches_WritesItsStatusHeadersAndMembers(bool useHandler)
    {
        using var host = await ExceptionHost.StartAsync(
            useHandler, _ => throw new UnauthorizedAccessException("hidden"), services =>
            {
                services.AddBsftExceptionMapper<UnauthorizedMapper>();
                services.AddBsftExceptionHandler();
            });

        var response = await host.GetTestClient().GetAsync("/");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.GetValues("Retry-After").Should().Equal("30");
        body.RootElement.GetProperty("type").GetString().Should().Be("https://example.test/problems/unauthorized");
        body.RootElement.GetProperty("title").GetString().Should().Be("Unauthorized");
        body.RootElement.GetProperty("detail").GetString().Should().Be("Sign in first.");
        body.RootElement.GetProperty("errorCode").GetString().Should().Be("AUTH_REQUIRED");
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(ExceptionHost.CorrelationId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_AppMapperDeclines_FallsBackToTheBuiltInMapping(bool useHandler)
    {
        using var host = await ExceptionHost.StartAsync(
            useHandler, _ => throw new ForbiddenException("no scope"), services =>
            {
                services.AddBsftExceptionMapper<UnauthorizedMapper>();
                services.AddBsftExceptionHandler();
            });

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Headers.Contains("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task Respond_AppMapperClaimsAFrameworkException_RunsBeforeTheBuiltInMapping()
    {
        using var host = await ExceptionHost.StartAsync(
            false, _ => throw new ForbiddenException("no scope"),
            services => services.AddBsftExceptionMapper<CatchAllConflictMapper>());

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Should().Contain("Late Conflict").And.NotContain("no scope");
    }

    [Fact]
    public async Task Respond_TwoMappersMatch_TheFirstRegisteredWins()
    {
        using var host = await ExceptionHost.StartAsync(
            false, _ => throw new UnauthorizedAccessException(), services =>
            {
                services.AddBsftExceptionMapper<UnauthorizedMapper>();
                services.AddBsftExceptionMapper<CatchAllConflictMapper>();
            });

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Respond_AppMapperMapsA500_NeverPublishesTheExceptionText()
    {
        using var host = await ExceptionHost.StartAsync(
            false, _ => throw new NotSupportedException("secret table name"),
            services => services.AddBsftExceptionMapper<LeakyInternalErrorMapper>());

        var response = await host.GetTestClient().GetAsync("/");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        body.Should().NotContain("secret table name");
        body.Should().Contain("Custom Failure").And.Contain("CUSTOM").And.Contain("An unexpected error occurred.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_ObserverRegistered_IsToldTheExceptionTheResultAndTheContext(bool useHandler)
    {
        var failure = new ForbiddenException("no scope");
        using var host = await ExceptionHost.StartAsync(
            useHandler, _ => throw failure, services =>
            {
                services.AddBsftExceptionObserver<RecordingExceptionObserver>();
                services.AddBsftExceptionHandler();
            });

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var observer = host.Services.GetServices<IBsftExceptionObserver>().OfType<RecordingExceptionObserver>().Single();
        var told = observer.Told.Should().ContainSingle().Subject;
        told.Exception.Should().BeSameAs(failure);
        told.Result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        told.Context.Should().NotBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_ObserverThrows_ResponseIsUnchangedAndTheFailureIsReported(bool useHandler)
    {
        var reporter = new CountingFailureReporter();
        using var host = await ExceptionHost.StartAsync(
            useHandler, _ => throw new ForbiddenException("no scope"), services =>
            {
                services.AddSingleton<IObserverFailureReporter>(reporter);
                services.AddBsftExceptionObserver<ThrowingExceptionObserver>();
                services.AddBsftExceptionObserver<RecordingExceptionObserver>();
                services.AddBsftExceptionHandler();
            });

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        reporter.Reported.Should().Be(1);
        host.Services.GetServices<IBsftExceptionObserver>()
            .OfType<RecordingExceptionObserver>().Single().Told.Should().ContainSingle("the observers after a failing one are still told");
    }

    [Fact]
    public async Task Respond_ResponseAlreadyStarted_StillTellsTheObserver()
    {
        using var host = await ExceptionHost.StartAsync(
            false, async context =>
            {
                await context.Response.WriteAsync("partial");
                await context.Response.Body.FlushAsync();
                throw new ForbiddenException("late");
            },
            services =>
            {
                services.AddBsftExceptionObserver<RecordingExceptionObserver>();
            });

        var response = await host.GetTestClient().GetAsync("/");

        (await response.Content.ReadAsStringAsync()).Should().Be("partial");
        host.Services.GetServices<IBsftExceptionObserver>().OfType<RecordingExceptionObserver>().Single().Told.Should().ContainSingle();
    }

    [Fact]
    public void AddBsftExceptionMapper_RegisteredTwice_IsRegisteredOnce()
    {
        var services = new ServiceCollection();

        services.AddBsftExceptionMapper<UnauthorizedMapper>().AddBsftExceptionMapper<UnauthorizedMapper>();

        services.Count(descriptor => descriptor.ServiceType == typeof(IBsftExceptionMapper)).Should().Be(1);
    }

    [Fact]
    public void AddBsftExceptionMapper_NullServices_Throws() =>
        ((Action)(() => ExceptionServiceCollectionExtensions.AddBsftExceptionMapper<UnauthorizedMapper>(null!)))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void AddBsftExceptionObserver_NullServices_Throws() =>
        ((Action)(() => ExceptionServiceCollectionExtensions.AddBsftExceptionObserver<RecordingExceptionObserver>(null!)))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void AddBsftExceptionHandler_NullServices_Throws() =>
        ((Action)(() => ExceptionServiceCollectionExtensions.AddBsftExceptionHandler(null!)))
            .Should().Throw<ArgumentNullException>();
}
