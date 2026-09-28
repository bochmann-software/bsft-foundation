using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>One third of a dependency cycle: it waits for <c>CyclicB</c>.</summary>
internal sealed class CyclicModuleC : IModule
{
    public string Name => "CyclicC";
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn => ["CyclicB"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
