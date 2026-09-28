using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests.Pagination;

/// <summary>
/// The request and result shapes a client speaks the query surface in. The defaults are part of the
/// contract: a caller that states nothing still has to get a bounded page.
/// </summary>
public sealed class QueryShapeTests
{
    [Fact]
    public void A_request_that_states_nothing_asks_for_the_first_page_at_the_default_size()
    {
        var request = new PagedRequest();

        request.Filter.Should().BeNull();
        request.Sort.Should().BeNull();
        request.Search.Should().BeNull();
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(PagedRequest.DefaultPageSize);
        PagedRequest.DefaultPageSize.Should().Be(25);
    }

    [Fact]
    public void A_request_carries_the_filter_the_order_the_search_and_the_page_it_was_given()
    {
        var filter = new PresenceNode { Field = "email" };
        var sort = new[] { new SortItem("name"), new SortItem("age", SortDirection.Desc) };

        var request = new PagedRequest(filter, sort, 3, 50)
        {
            Search = new SearchRequest("people", "ada")
        };

        request.Filter.Should().BeSameAs(filter);
        request.Sort.Should().Equal(sort);
        request.Page.Should().Be(3);
        request.PageSize.Should().Be(50);
        request.Search!.Group.Should().Be("people");
        request.Search.Term.Should().Be("ada");
    }

    /// <summary>The search is an init-only property, so it must survive a with-expression.</summary>
    [Fact]
    public void A_request_keeps_its_search_when_another_field_is_changed()
        => (new PagedRequest { Search = new SearchRequest("people", "ada") } with { Page = 2 })
            .Search
            .Should()
            .Be(new SearchRequest("people", "ada"));

    [Fact]
    public void A_sort_criterion_without_a_direction_runs_ascending()
    {
        var item = new SortItem("name");

        item.Field.Should().Be("name");
        item.Direction.Should().Be(SortDirection.Asc);
    }

    [Fact]
    public void A_search_request_ranks_by_relevance_and_lets_the_store_choose_the_mode()
    {
        var search = new SearchRequest("people", "ada");

        search.Mode.Should().BeNull();
        search.IsOrderedByRank.Should().BeTrue();
    }

    [Fact]
    public void A_page_says_what_is_on_it_how_many_there_are_and_whether_another_follows()
    {
        var page = new PagedResult<string>(["a", "b"], 7, 1, 2, HasNextPage: true);

        page.Items.Should().Equal("a", "b");
        page.TotalCount.Should().Be(7);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(2);
        page.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void A_hit_carries_its_entity_and_its_score()
    {
        var hit = new RankedItem<string> { Item = "ada", Score = 0.75 };

        hit.Item.Should().Be("ada");
        hit.Score.Should().Be(0.75);
    }
}
