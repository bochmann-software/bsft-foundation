using Bsft.Data.DependencyInjection;
using Bsft.Data.KeysetPaging;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.DependencyInjection;

public sealed class KeysetPagingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddBsftDataKeysetPaging_Registered_ResolvesReaderAndSerializerAsSingletons()
    {
        using var provider = new ServiceCollection().AddBsftDataKeysetPaging().BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IKeysetPageReader>().Should().BeSameAs(provider.GetRequiredService<IKeysetPageReader>());
        provider.GetRequiredService<IKeysetCursorSerializer>().Should().BeOfType<KeysetCursorSerializer>()
            .And.BeSameAs(provider.GetRequiredService<IKeysetCursorSerializer>());
        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.KeysetPaging.MaxPageSize
            .Should().Be(DataDefaults.KeysetMaxPageSize);
    }

    [Fact]
    public void AddBsftDataKeysetPaging_Configure_SetsTheMaximum()
    {
        using var provider = new ServiceCollection()
            .AddBsftDataKeysetPaging(options => options.MaxPageSize = 500)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.KeysetPaging.MaxPageSize.Should().Be(500);
    }

    [Fact]
    public void AddBsftDataKeysetPaging_Configuration_BindsTheSectionThenRunsConfigure()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bsft:Data:KeysetPaging:MaxPageSize"] = "250" })
            .Build();
        int? bound = null;

        using var provider = new ServiceCollection()
            .AddBsftDataKeysetPaging(configuration, options => bound = options.MaxPageSize)
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.KeysetPaging.MaxPageSize.Should().Be(250);
        bound.Should().Be(250);
    }

    [Fact]
    public void AddBsftDataKeysetPaging_ConfigurationWithoutSection_KeepsTheDefault()
    {
        using var provider = new ServiceCollection()
            .AddBsftDataKeysetPaging(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        provider.GetRequiredService<IOptions<BsftDataOptions>>().Value.KeysetPaging.MaxPageSize
            .Should().Be(DataDefaults.KeysetMaxPageSize);
    }

    [Fact]
    public void AddBsftDataKeysetPaging_NullConfiguration_Throws()
    {
        var act = () => new ServiceCollection().AddBsftDataKeysetPaging((IConfiguration)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void AddBsftDataKeysetPaging_ConfigurationOverloadNullServices_Throws()
    {
        var act = () => KeysetPagingServiceCollectionExtensions.AddBsftDataKeysetPaging(null!, new ConfigurationBuilder().Build());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddBsftDataKeysetPaging_InvalidMaximum_IsRefusedByTheOptionsValidation()
    {
        using var provider = new ServiceCollection()
            .AddBsftDataKeysetPaging(options => options.MaxPageSize = 0)
            .BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<BsftDataOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*MaxPageSize*");
    }

    [Fact]
    public void AddBsftDataKeysetPaging_SerializerRegisteredFirst_KeepsTheHostsSerializer()
    {
        var own = new KeysetCursorSerializer();
        var services = new ServiceCollection().AddSingleton<IKeysetCursorSerializer>(own);

        using var provider = services.AddBsftDataKeysetPaging().BuildServiceProvider();

        provider.GetRequiredService<IKeysetCursorSerializer>().Should().BeSameAs(own);
    }

    [Fact]
    public void AddBsftDataKeysetPaging_NullServices_Throws()
    {
        var act = () => KeysetPagingServiceCollectionExtensions.AddBsftDataKeysetPaging(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}
