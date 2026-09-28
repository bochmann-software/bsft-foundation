using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>A module depending on <c>TestModuleA</c>, so an order has somewhere to go wrong.</summary>
internal sealed class TestModuleB : IModule
{
    public string Name => "TestModuleB";
    public string Version => "1.1.0";
    public IReadOnlyList<string> DependsOn => ["TestModuleA"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;

    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
