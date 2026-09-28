using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Bsft.Hosting.Tests.Filtering;

public class FilterNodeModelBinderTests
{
    private static DefaultModelBindingContext CreateContext(string? filterValue, IFilterQueryParser parser)
    {
        var services = new ServiceCollection();
        services.AddSingleton(parser);

        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (filterValue is not null)
            httpContext.Request.QueryString = new QueryString("?filter=" + Uri.EscapeDataString(filterValue));

        var metadataProvider = new EmptyModelMetadataProvider();
        return new DefaultModelBindingContext
        {
            ModelName = "Filter",
            ModelState = new ModelStateDictionary(),
            ModelMetadata = metadataProvider.GetMetadataForType(typeof(FilterNode)),
            ActionContext = new Microsoft.AspNetCore.Mvc.ActionContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task BindModelAsync_NullContext_Throws()
    {
        var binder = new FilterNodeModelBinder();

        var act = () => binder.BindModelAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("bindingContext");
    }

    [Fact]
    public async Task BindModelAsync_NoFilterKey_BindsNull()
    {
        var parser = Substitute.For<IFilterQueryParser>();
        parser.Parse(Arg.Any<string?>()).Returns((FilterNode?)null);
        var context = CreateContext(filterValue: null, parser);
        var binder = new FilterNodeModelBinder();

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().BeNull();
    }

    [Fact]
    public async Task BindModelAsync_ValidFilter_BindsParsedNode()
    {
        var node = new ComparisonNode
        {
            Field = "Name",
            Op = FilterOperator.Eq,
            Value = System.Text.Json.JsonSerializer.SerializeToElement("Alice")
        };
        var parser = Substitute.For<IFilterQueryParser>();
        parser.Parse("{json}").Returns(node);
        var context = CreateContext("{json}", parser);
        var binder = new FilterNodeModelBinder();

        await binder.BindModelAsync(context);

        context.Result.IsModelSet.Should().BeTrue();
        context.Result.Model.Should().BeSameAs(node);
    }

    [Fact]
    public async Task BindModelAsync_ParserThrows_PropagatesValidationException()
    {
        var parser = Substitute.For<IFilterQueryParser>();
        parser.Parse(Arg.Any<string?>())
            .Returns(_ => throw new ValidationException(new Dictionary<string, string[]> { ["filter"] = ["bad"] }));
        var context = CreateContext("{bad", parser);
        var binder = new FilterNodeModelBinder();

        var act = () => binder.BindModelAsync(context);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
