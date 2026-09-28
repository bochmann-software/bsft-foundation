using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>A module depending on both of the others, so an order has three places to go wrong.</summary>
internal sealed class TestModuleC : IModule
{
    public string Name => "TestModuleC";
    public string Version => "2.0.0";
    public IReadOnlyList<string> DependsOn => ["TestModuleA", "TestModuleB"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
