using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>One third of a dependency cycle: it waits for <c>CyclicA</c>.</summary>
internal sealed class CyclicModuleB : IModule
{
    public string Name => "CyclicB";
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn => ["CyclicA"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
