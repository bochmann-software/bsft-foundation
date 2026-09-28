using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Modules.Tests.Fakes;

/// <summary>A module with no dependencies, and the one every ordering test starts from.</summary>
internal sealed class TestModuleA : IModule
{
    public string Name => "TestModuleA";
    public string Version => "1.0.0";
    public IReadOnlyList<string> DependsOn => [];

    public void ConfigureServices(IServiceCollection services, IConfiguration config) { }

    public Task OnStartAsync(IServiceProvider services, CancellationToken ct = default) => Task.CompletedTask;

    public Task OnStopAsync(CancellationToken ct = default) => Task.CompletedTask;
}
