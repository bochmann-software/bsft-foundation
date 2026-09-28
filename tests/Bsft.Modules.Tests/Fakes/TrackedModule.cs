using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>A module that appends its own name to a shared log as it starts and stops, so a
/// lifecycle order can be read back as a list.</summary>
internal sealed class TrackedModule : IModule
{
    private readonly List<string> _tracker;

    // Parameterless constructor required for Assembly.LoadFrom scanning in ModuleCatalog.FromDirectory
    public TrackedModule()
    {
        Name = "TrackedModule";
        DependsOn = [];
        _tracker = [];
    }

    public TrackedModule(string name, string[] dependsOn, List<string> tracker)
    {
        Name = name;
        DependsOn = dependsOn;
        _tracker = tracker;
    }

    public string Name { get; }
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn { get; }

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default)
    {
        _tracker.Add($"{Name}:start");
        return Task.CompletedTask;
    }

    public Task OnStopAsync(CancellationToken ct = default)
    {
        _tracker.Add($"{Name}:stop");
        return Task.CompletedTask;
    }
}
