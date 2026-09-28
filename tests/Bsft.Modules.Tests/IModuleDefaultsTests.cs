using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Modules.Tests;

public class IModuleDefaultsTests
{
    /// <summary>
    /// A minimal IModule implementation that relies on the default DependsOn implementation.
    /// </summary>
    private class MinimalModule : IModule
    {
        public string Name => "Minimal";
        public string Version => "1.0.0";

        // DependsOn is NOT overridden - uses the default interface implementation

        public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
        public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
        public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void DependsOn_DefaultImplementation_ReturnsEmptyArray()
    {
        IModule module = new MinimalModule();

        module.DependsOn.Should().BeEmpty();
    }
}
