using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>One third of a dependency cycle: it waits for <c>CyclicC</c>.</summary>
internal sealed class CyclicModuleA : IModule
{
    public string Name => "CyclicA";
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn => ["CyclicC"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
