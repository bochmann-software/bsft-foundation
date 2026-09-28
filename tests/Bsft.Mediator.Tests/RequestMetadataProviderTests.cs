using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

public class RequestMetadataProviderTests
{
    [Fact]
    public void GetMetadata_Instance_ReportsConcreteRequestType()
    {
        var provider = new RequestMetadataProvider();

        var metadata = provider.GetMetadata(new MetadataProbeCommand());

        metadata.RequestType.Should().Be<MetadataProbeCommand>();
    }

    [Fact]
    public void GetMetadata_Instance_ReadsAttributeDeclaredOnTheRequest()
    {
        var provider = new RequestMetadataProvider();

        var attribute = provider.GetMetadata(new MetadataProbeCommand()).FindAttribute<UsageAttribute>();

        attribute.Should().NotBeNull();
        attribute!.Feature.Should().Be("probe");
    }

    [Fact]
    public void GetAttribute_RequestWithoutTheAttribute_ReturnsNull()
    {
        var provider = new RequestMetadataProvider();

        provider.GetMetadata(typeof(UnmarkedCommand)).FindAttribute<UsageAttribute>().Should().BeNull();
    }

    [Fact]
    public void GetAttributes_AttributeAppliedSeveralTimes_ReturnsAllOfThem()
    {
        var provider = new RequestMetadataProvider();

        var capabilities = provider
            .GetMetadata(typeof(MultiCapabilityCommand))
            .ListAttributes<CapabilityAttribute>()
            .Select(attribute => attribute.Name);

        capabilities.Should().BeEquivalentTo(["read", "write"]);
    }

    [Fact]
    public void GetAttributes_RequestWithoutTheAttribute_ReturnsEmpty()
    {
        var provider = new RequestMetadataProvider();

        provider.GetMetadata(typeof(UnmarkedCommand)).ListAttributes<CapabilityAttribute>().Should().BeEmpty();
    }

    [Fact]
    public void HasAttribute_ReportsWhetherTheRequestDeclaresIt()
    {
        var provider = new RequestMetadataProvider();
        var metadata = provider.GetMetadata(typeof(MetadataProbeCommand));

        metadata.HasAttribute<UsageAttribute>().Should().BeTrue();
        metadata.HasAttribute<CapabilityAttribute>().Should().BeFalse();
    }

    [Fact]
    public void GetMetadata_AttributeOnABaseRequest_IsVisibleOnTheDerivedOne()
    {
        var provider = new RequestMetadataProvider();

        provider.GetMetadata(typeof(DerivedProbeCommand)).FindAttribute<UsageAttribute>().Should().NotBeNull();
    }

    [Fact]
    public void GetMetadata_SameRequestType_ReturnsTheSameDescriptorInstance()
    {
        var provider = new RequestMetadataProvider();

        var first = provider.GetMetadata(new MetadataProbeCommand());
        var second = provider.GetMetadata(new MetadataProbeCommand());
        var byType = provider.GetMetadata(typeof(MetadataProbeCommand));

        second.Should().BeSameAs(first);
        byType.Should().BeSameAs(first);
    }

    [Fact]
    public void GetMetadata_DifferentRequestTypes_ReturnDifferentDescriptors()
    {
        var provider = new RequestMetadataProvider();

        provider.GetMetadata(typeof(MetadataProbeCommand))
            .Should().NotBeSameAs(provider.GetMetadata(typeof(UnmarkedCommand)));
    }

    [Fact]
    public void GetMetadata_ResolvedConcurrently_HandsEveryCallerTheSameDescriptor()
    {
        var provider = new RequestMetadataProvider();
        var descriptors = new IRequestMetadata[64];

        Parallel.For(0, descriptors.Length, index =>
            descriptors[index] = provider.GetMetadata(new MetadataProbeCommand()));

        descriptors.Should().OnlyContain(descriptor => ReferenceEquals(descriptor, descriptors[0]));
    }

    [Fact]
    public void GetMetadata_NullRequest_Throws()
    {
        var provider = new RequestMetadataProvider();

        var act = () => provider.GetMetadata((object)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public void GetMetadata_NullRequestType_Throws()
    {
        var provider = new RequestMetadataProvider();

        var act = () => provider.GetMetadata((Type)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("requestType");
    }

    [Fact]
    public void RequestMetadata_NullRequestType_Throws()
    {
        var act = () => new RequestMetadata(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("requestType");
    }

    [Fact]
    public void AddBsftMediator_RegistersTheProviderAsASingleton()
    {
        var services = new ServiceCollection();
        services.AddBsftMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(MetadataProbeHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        var provider = sp.GetService<IRequestMetadataProvider>();

        provider.Should().BeOfType<RequestMetadataProvider>();
        sp.GetService<IRequestMetadataProvider>().Should().BeSameAs(provider);
    }

    [Fact]
    public void AddBsftMediator_ProviderAlreadyRegistered_KeepsTheHostsOwn()
    {
        var services = new ServiceCollection();
        var own = new RequestMetadataProvider();
        services.AddSingleton<IRequestMetadataProvider>(own);

        services.AddBsftMediator(cfg => cfg.RegisterServicesFromAssembly(typeof(MetadataProbeHandler).Assembly));

        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IRequestMetadataProvider>().Should().BeSameAs(own);
    }

    /// <summary>
    /// The defect this seam closes: the pipeline is closed over <see cref="ICommand{TResult}"/>, so a
    /// behavior registered against the marker interface sees the attribute only through the metadata
    /// provider — reading it off the type parameter finds nothing.
    /// </summary>
    [Fact]
    public async Task Dispatch_BehaviorOnTheMarkerInterface_SeesTheAttributeOnTheConcreteRequest()
    {
        var recorder = new UsageRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddBsftMediator(cfg => cfg
            .RegisterServicesFromAssembly(typeof(MetadataProbeHandler).Assembly)
            .AddBehavior(typeof(UsageRecordingBehavior<,>)));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new MetadataProbeCommand());

        recorder.Features.Should().Equal("probe");
        recorder.RequestTypes.Should().Equal(typeof(MetadataProbeCommand));
    }

    [Fact]
    public async Task Dispatch_UnmarkedRequest_LeavesTheBehaviorWithNothingToRecord()
    {
        var recorder = new UsageRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddBsftMediator(cfg => cfg
            .RegisterServicesFromAssembly(typeof(MetadataProbeHandler).Assembly)
            .AddBehavior(typeof(UsageRecordingBehavior<,>)));

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        await mediator.SendAsync(new UnmarkedCommand());

        recorder.Features.Should().BeEmpty();
        recorder.RequestTypes.Should().Equal(typeof(UnmarkedCommand));
    }
}
