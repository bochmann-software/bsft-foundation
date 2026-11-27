using System.Globalization;
using System.Reflection;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Bsft.Correlation;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests.Filtering;

/// <summary>
/// Tests for <see cref="PagedQueryParameters"/> binding: paging, sorting, search, and filter
/// query parameters for minimal-API handlers.
/// </summary>
public class PagedQueryParametersTests
{
    private static ParameterInfo GetDummyParameter()
        => typeof(PagedQueryParametersTests)
            .GetMethod(nameof(DummyMethod), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetParameters()[0];

    private static void DummyMethod(PagedQueryParameters _) { }

    private static DefaultHttpContext CreateContext(string? queryString = null, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICorrelationContext>(_ =>
        {
            var ctx = Substitute.For<ICorrelationContext>();
            ctx.CorrelationId.Returns("test-correlation-id");
            return ctx;
        });
        services.AddBsftFilterTransport();
        configureServices?.Invoke(services);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (queryString is not null)
            context.Request.QueryString = new QueryString(queryString);
        return context;
    }

    private sealed record ListEcho(
        int? Page,
        int? PageSize,
        int? SortCount,
        string? FilterType,
        string? SearchGroup,
        string? SearchTerm,
        string? SearchMode,
        bool? SearchIsOrderedByRank);

    private static async Task<IHost> CreateHostAsync(Action<BsftFilterTransportOptions>? configure = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddScoped<ICorrelationContext>(_ =>
                    {
                        var ctx = Substitute.For<ICorrelationContext>();
                        ctx.CorrelationId.Returns("test-correlation-id");
                        return ctx;
                    });
                    services.AddBsftFilterTransport(configure);
                })
                .Configure(app =>
                {
                    app.UseMiddleware<BsftExceptionMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/list", Handle);
                    });
                });
            })
            .StartAsync();

        async Task<IResult> Handle(PagedQueryParameters query)
        {
            return Results.Ok(new ListEcho(
                query.Request.Page,
                query.Request.PageSize,
                query.Request.Sort?.Count,
                query.Request.Filter?.GetType().Name,
                query.Request.Search?.Group,
                query.Request.Search?.Term,
                query.Request.Search?.Mode?.ToString(),
                query.Request.Search?.IsOrderedByRank));
        }
    }

    [Fact]
    public async Task BindAsync_EmptyQuery_BindsDefaults()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/list");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<ListEcho>();
        echo!.Page.Should().Be(1);
        echo.PageSize.Should().Be(25);
        echo.SortCount.Should().BeNull();
        echo.FilterType.Should().BeNull();
        echo.SearchGroup.Should().BeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task BindAsync_PageAndPageSizeQuery_BindsValues()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/list?page=3&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = await response.Content.ReadFromJsonAsync<ListEcho>();
        echo!.Page.Should().Be(3);
        echo.PageSize.Should().Be(50);

        await host.StopAsync();
    }

    [Fact]
    public async Task BindAsync_QueryKeysCaseInsensitive_BindsValues()
    {
        var context = CreateContext("?PAGE=2&PAGESIZE=30");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Page.Should().Be(2);
        result.Request.PageSize.Should().Be(30);
    }

    [Fact]
    public async Task BindAsync_PageZero_IsRejectedOnPage()
    {
        var context = CreateContext("?page=0");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public async Task BindAsync_PageNegative_IsRejectedOnPage()
    {
        var context = CreateContext("?page=-1");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public async Task BindAsync_PageSizeZero_IsRejectedOnPageSize()
    {
        var context = CreateContext("?pageSize=0");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public async Task BindAsync_PageSizeAboveDefault_IsRejectedOnPageSize()
    {
        var context = CreateContext("?pageSize=101");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public async Task BindAsync_PageSizeEqualToDefaultMax_IsAccepted()
    {
        var context = CreateContext("?pageSize=100");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task BindAsync_PageSizeWithCustomMaxPageSizeOption_IsRespected()
    {
        var context = CreateContext("?pageSize=500", services => services.Configure<BsftFilterTransportOptions>(o => o.MaxPageSize = 500));

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.PageSize.Should().Be(500);
    }

    [Fact]
    public async Task BindAsync_PageSizeAboveCustomMaxPageSize_IsRejectedOnPageSize()
    {
        var context = CreateContext("?pageSize=501", services => services.Configure<BsftFilterTransportOptions>(o => o.MaxPageSize = 500));

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public async Task BindAsync_WithoutAPageSizeAndAMaxPageSizeBelowTheDefault_UsesTheMaxPageSize()
    {
        var context = CreateContext(null, services => services.Configure<BsftFilterTransportOptions>(o => o.MaxPageSize = 10));

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task BindAsync_PageNonInteger_IsRejectedOnPage()
    {
        var context = CreateContext("?page=abc");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public async Task BindAsync_PageDecimal_IsRejectedOnPage()
    {
        var context = CreateContext("?page=1.5");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task BindAsync_PageMultipleValues_IsRejectedOnPage()
    {
        var context = CreateContext("?page=1&page=2");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public async Task BindAsync_WithARepeatedFilter_IsRejectedUnderItsKey()
    {
        var filter1 = Uri.EscapeDataString("""{ "$type": "comparison", "field": "Name", "op": "eq", "value": "A" }""");
        var filter2 = Uri.EscapeDataString("""{ "$type": "comparison", "field": "Name", "op": "eq", "value": "B" }""");
        var context = CreateContext($"?filter={filter1}&filter={filter2}");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("filter");
    }

    [Fact]
    public async Task BindAsync_WithARepeatedSortField_IsRejectedUnderItsKey()
    {
        var context = CreateContext("?sort[0].field=name&sort[0].field=id");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort[0].field");
    }

    [Fact]
    public async Task BindAsync_WithARepeatedSearchGroup_IsRejectedUnderItsKey()
    {
        var context = CreateContext("?search.group=a&search.group=b&search.term=t");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.group");
    }

    [Fact]
    public async Task BindAsync_WithARepeatedSearchTerm_IsRejectedUnderItsKey()
    {
        var context = CreateContext("?search.group=g&search.term=a&search.term=b");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.term");
    }

    [Fact]
    public async Task BindAsync_PageHugeWithOverflow_IsRejectedOnPage()
    {
        var context = CreateContext("?page=21474837&pageSize=100");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("page");
    }

    [Fact]
    public async Task BindAsync_PageHugeWithoutOverflow_IsAccepted()
    {
        var context = CreateContext("?page=21474836&pageSize=100");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindAsync_SortWithFieldAndDirection_Binds()
    {
        var context = CreateContext("?sort[0].field=name&sort[0].direction=desc&sort[1].field=id");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Sort.Should().HaveCount(2);
    }

    [Fact]
    public async Task BindAsync_SortDirectionCaseInsensitive_Binds()
    {
        var context = CreateContext("?sort[0].field=name&sort[0].direction=DESC");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindAsync_SortInvalidDirection_IsRejectedOnDirection()
    {
        var context = CreateContext("?sort[0].field=name&sort[0].direction=sideways");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort[0].direction");
    }

    [Fact]
    public async Task BindAsync_SortNumericDirection_IsRejectedOnDirection()
    {
        var context = CreateContext("?sort[0].field=name&sort[0].direction=2");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task BindAsync_SortWithoutField_IsRejectedOnField()
    {
        var context = CreateContext("?sort[0].direction=asc");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort[0].field");
    }

    [Fact]
    public async Task BindAsync_WithAGapInTheSortIndexes_IsRejectedUnderSort()
    {
        var context = CreateContext("?sort[0].field=a&sort[2].field=b");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort");
    }

    [Fact]
    public async Task BindAsync_WithASortIndexThatIsNoNumber_IsRejectedUnderSort()
    {
        var context = CreateContext("?sort[x].field=a");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort");
    }

    [Fact]
    public async Task BindAsync_WithASortStartingAtOne_IsRejectedUnderSort()
    {
        var context = CreateContext("?sort[1].field=a");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort");
    }

    [Fact]
    public async Task BindAsync_WithConsecutiveSortEntries_BindsTwoEntries()
    {
        var context = CreateContext("?sort[0].field=name&sort[1].field=id");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Sort.Should().HaveCount(2);
    }

    [Fact]
    public async Task BindAsync_SortMaxCount16_IsAccepted()
    {
        var query = string.Join("&", Enumerable.Range(0, 16).Select(i => $"sort[{i}].field=f{i}"));
        var context = CreateContext($"?{query}");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Sort.Should().HaveCount(16);
    }

    [Fact]
    public async Task BindAsync_SortExceedsMaxCount_IsRejectedOnSort()
    {
        var query = string.Join("&", Enumerable.Range(0, 17).Select(i => $"sort[{i}].field=f{i}"));
        var context = CreateContext($"?{query}");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("sort");
    }

    [Fact]
    public async Task BindAsync_SearchWithGroupAndTerm_Binds()
    {
        var context = CreateContext("?search.group=g&search.term=t");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Search.Should().NotBeNull();
        result.Request.Search!.Group.Should().Be("g");
        result.Request.Search.Term.Should().Be("t");
        result.Request.Search.IsOrderedByRank.Should().Be(true);
    }

    [Fact]
    public async Task BindAsync_SearchWithMode_Binds()
    {
        var context = CreateContext("?search.group=g&search.term=t&search.mode=Prefix");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Search!.Mode.Should().Be(SearchMode.Prefix);
    }

    [Fact]
    public async Task BindAsync_SearchWithIsOrderedByRankFalse_Binds()
    {
        var context = CreateContext("?search.group=g&search.term=t&search.isOrderedByRank=false");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Search!.IsOrderedByRank.Should().Be(false);
    }

    [Fact]
    public async Task BindAsync_SearchWithoutGroup_IsRejectedOnGroup()
    {
        var context = CreateContext("?search.term=t");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.group");
    }

    [Fact]
    public async Task BindAsync_SearchWithoutTerm_IsRejectedOnTerm()
    {
        var context = CreateContext("?search.group=g");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.term");
    }

    [Fact]
    public async Task BindAsync_SearchOnlyMode_IsRejectedOnGroup()
    {
        var context = CreateContext("?search.mode=Prefix");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.group");
    }

    [Fact]
    public async Task BindAsync_SearchInvalidIsOrderedByRank_IsRejectedOnIsOrderedByRank()
    {
        var context = CreateContext("?search.group=g&search.term=t&search.isOrderedByRank=maybe");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.isOrderedByRank");
    }

    [Fact]
    public async Task BindAsync_SearchInvalidMode_IsRejectedOnMode()
    {
        var context = CreateContext("?search.group=g&search.term=t&search.mode=InvalidMode");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("search.mode");
    }

    [Fact]
    public async Task BindAsync_ValidJsonFilter_Binds()
    {
        var filter = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" }""";
        var context = CreateContext("?filter=" + Uri.EscapeDataString(filter));

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Filter.Should().NotBeNull();
        result.Request.Filter!.GetType().Name.Should().Be("ComparisonNode");
    }

    [Fact]
    public async Task BindAsync_WhitespaceOnlyFilter_ReturnsNullFilter()
    {
        var context = CreateContext("?filter=  ");

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
        result!.Request.Filter.Should().BeNull();
    }

    [Fact]
    public async Task BindAsync_NoFilterWithoutParserRegistered_Succeeds()
    {
        var context = CreateContext(null);

        var result = await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task BindAsync_FilterPresentWithoutParserRegistered_ThrowsInvalidOperationException()
    {
        var filter = """{ "$type": "comparison", "field": "Name", "op": "eq", "value": "Alice" }""";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICorrelationContext>(_ =>
        {
            var ctx = Substitute.For<ICorrelationContext>();
            ctx.CorrelationId.Returns("test-correlation-id");
            return ctx;
        });
        var serviceProvider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = serviceProvider };
        context.Request.QueryString = new QueryString("?filter=" + Uri.EscapeDataString(filter));

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task BindAsync_OptionsNotRegisteredAtAll_UsesDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICorrelationContext>(_ =>
        {
            var ctx = Substitute.For<ICorrelationContext>();
            ctx.CorrelationId.Returns("test-correlation-id");
            return ctx;
        });
        services.AddBsftFilterTransport();

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.QueryString = new QueryString("?pageSize=101");

        var act = async () => await PagedQueryParameters.BindAsync(context, GetDummyParameter());

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public void BindAsync_NullContext_ThrowsArgumentNullException()
    {
        var act = () => PagedQueryParameters.BindAsync(null!, GetDummyParameter());

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("context");
    }

    [Fact]
    public void Constructor_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => new PagedQueryParameters(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constants_Always_HaveTheDocumentedValues()
    {
        PagedQueryParameters.PageKey.Should().Be("page");
        PagedQueryParameters.PageSizeKey.Should().Be("pageSize");
        PagedQueryParameters.MaxSortCount.Should().Be(16);
    }
}
