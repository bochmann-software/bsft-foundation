using Bsft.Correlation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Bsft.Hosting.Tests.Support;

/// <summary>A test server whose pipeline answers failures through the middleware or through the exception handler.</summary>
internal static class ExceptionHost
{
    public const string CorrelationId = "test-correlation-id";

    public static async Task<IHost> StartAsync(
        bool useHandler, RequestDelegate endpoint, Action<IServiceCollection>? configureServices = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddScoped<ICorrelationContext>(_ =>
                    {
                        var correlation = Substitute.For<ICorrelationContext>();
                        correlation.CorrelationId.Returns(CorrelationId);
                        return correlation;
                    });
                    configureServices?.Invoke(services);
                });
                webBuilder.Configure(app =>
                {
                    if (useHandler)
                        app.UseExceptionHandler(handlerApp => handlerApp.Run(_ => Task.CompletedTask));
                    else
                        app.UseMiddleware<BsftExceptionMiddleware>();

                    app.Run(endpoint);
                });
            })
            .StartAsync();
    }
}
