using Bsft.Data.Contracts.Pagination;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests.Pagination;

/// <summary>The keyset request: a caller that states nothing gets the first page at the default size.</summary>
public sealed class CursorPagedRequestTests
{
    [Fact]
    public void Constructor_NothingStated_AsksForTheFirstPageAtTheDefaultSizeWithoutTotal()
    {
        var request = new CursorPagedRequest();

        request.Cursor.Should().BeNull();
        request.PageSize.Should().Be(PagedRequest.DefaultPageSize);
        request.IncludeTotalCount.Should().BeFalse();
    }

    [Fact]
    public void Constructor_CursorAndSize_CarriesWhatItWasGiven()
    {
        var request = new CursorPagedRequest("abc", 10) { IncludeTotalCount = true };

        request.Cursor.Should().Be("abc");
        request.PageSize.Should().Be(10);
        request.IncludeTotalCount.Should().BeTrue();
    }
}
