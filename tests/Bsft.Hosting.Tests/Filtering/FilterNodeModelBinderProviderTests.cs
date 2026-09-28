using Bsft.Data.Contracts.Filtering;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NSubstitute;
using Xunit;

namespace Bsft.Hosting.Tests.Filtering;

public class FilterNodeModelBinderProviderTests
{
    [Fact]
    public void Ctor_NullBinder_Throws()
    {
        var act = () => new FilterNodeModelBinderProvider(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("binder");
    }

    [Fact]
    public void GetBinder_NullContext_Throws()
    {
        var provider = new FilterNodeModelBinderProvider(new FilterNodeModelBinder());

        var act = () => provider.GetBinder(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public void GetBinder_FilterNodeType_ReturnsBinder()
    {
        var provider = new FilterNodeModelBinderProvider(new FilterNodeModelBinder());
        var context = Substitute.For<ModelBinderProviderContext>();
        var metadataProvider = new EmptyModelMetadataProvider();
        context.Metadata.Returns(metadataProvider.GetMetadataForType(typeof(FilterNode)));

        provider.GetBinder(context).Should().BeOfType<FilterNodeModelBinder>();
    }

    [Fact]
    public void GetBinder_OtherType_ReturnsNull()
    {
        var provider = new FilterNodeModelBinderProvider(new FilterNodeModelBinder());
        var context = Substitute.For<ModelBinderProviderContext>();
        var metadataProvider = new EmptyModelMetadataProvider();
        context.Metadata.Returns(metadataProvider.GetMetadataForType(typeof(string)));

        provider.GetBinder(context).Should().BeNull();
    }
}
