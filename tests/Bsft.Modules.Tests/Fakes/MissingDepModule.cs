using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>A module naming a dependency no catalog holds.</summary>
internal sealed class MissingDepModule : IModule
{
    public string Name => "MissingDep";
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn => ["NonExistent"];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }
    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;
    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
