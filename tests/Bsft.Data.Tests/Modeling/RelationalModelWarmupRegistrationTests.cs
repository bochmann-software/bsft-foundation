using Bsft.Data.DependencyInjection;
using Bsft.Data.Modeling;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.Modeling;

/// <summary>
/// Which entry points bring the warm-up along, that it is registered once however often they are
/// called, and that a real host has it listening before its other hosted services start.
/// </summary>
public sealed class RelationalModelWarmupRegistrationTests
{
    public static TheoryData<string> EntryPoints() =>
    [
        nameof(DataServiceCollectionExtensions.AddBsftDataModelWarmup),
        nameof(DataServiceCollectionExtensions.AddBsftDataQueryFiltering),
        nameof(DataServiceCollectionExtensions.AddBsftDataUnitOfWork),
        nameof(DataServiceCollectionExtensions.AddBsftDataMultiTenancy),
    ];

    [Fact]
    public void WithNullServicesThrows()
    {
        var act = () => DataServiceCollectionExtensions.AddBsftDataModelWarmup(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void EveryEntryPointRegistersTheWarmupOnce(string entryPoint)
    {
        var services = new ServiceCollection();

        Call(services, entryPoint);
        Call(services, entryPoint);
        services.AddBsftDataModelWarmup();

        services.Where(descriptor => descriptor.ImplementationType == typeof(RelationalModelWarmupHostedService))
            .Should().ContainSingle().Which.ServiceType.Should().Be<IHostedService>();
    }

    [Fact]
    public void IsOnByDefault()
    {
        using var provider = new ServiceCollection().AddBsftDataModelWarmup().BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.ShouldWarmRelationalModels.Should().BeTrue();
    }

    [Fact]
    public void IsTurnedOffFromTheDataSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Data:ShouldWarmRelationalModels"] = "false" })
            .Build();
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(configuration, tenancy => tenancy.ResolveFromHeader());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.ShouldWarmRelationalModels.Should().BeFalse();
    }

    /// <summary>
    /// A hosted service registered before the warm-up opens a context of a new model as it starts:
    /// the model is built by the time the context is initialized, because the host runs every
    /// <c>StartingAsync</c> before any <c>StartAsync</c>.
    /// </summary>
    [Fact]
    public async Task ListensBeforeTheFirstHostedServiceStarts()
    {
        var probe = new ProbeService();
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(probe);
                services.AddBsftDataModelWarmup();
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();

        probe.BuiltAtInitialization.Should().BeTrue();
    }

    private static void Call(IServiceCollection services, string entryPoint)
    {
        switch (entryPoint)
        {
            case nameof(DataServiceCollectionExtensions.AddBsftDataModelWarmup):
                services.AddBsftDataModelWarmup();
                break;
            case nameof(DataServiceCollectionExtensions.AddBsftDataQueryFiltering):
                services.AddBsftDataQueryFiltering(_ => { });
                break;
            case nameof(DataServiceCollectionExtensions.AddBsftDataUnitOfWork):
                services.AddBsftDataUnitOfWork<TestDbContext>();
                break;
            default:
                services.AddBsftDataMultiTenancy(tenancy => tenancy.ResolveFromHeader());
                break;
        }
    }

    /// <summary>Opens a context of a new model as it starts and records whether its model was built.</summary>
    private sealed class ProbeService : IHostedService
    {
        public bool BuiltAtInitialization { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            using var context = new FreshModelContext(Guid.NewGuid());
            BuiltAtInitialization = RelationalModelWarmup.IsBuilt(context.Model);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
