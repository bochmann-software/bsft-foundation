using System.Text.Json;
using Bsft.Data.Contracts.Pagination;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Contracts.Tests.Pagination;

/// <summary>
/// The keyset page as a client sees it. The absence of the next cursor is what marks the
/// last page, so a null cursor and a null total must not reach the JSON.
/// </summary>
public sealed class CursorPagedResultTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Serialize_LastPageWithoutTotal_LeavesOutNextCursorAndTotalCount()
    {
        var json = JsonSerializer.Serialize(new CursorPagedResult<int>([1, 2], 25, null), Web);

        json.Should().Be("""{"items":[1,2],"pageSize":25}""");
    }

    [Fact]
    public void Serialize_PageWithCursorAndTotal_WritesBoth()
    {
        var page = new CursorPagedResult<int>([1], 1, "next") { TotalCount = 3_000_000_000 };

        var json = JsonSerializer.Serialize(page, Web);

        json.Should().Be("""{"items":[1],"pageSize":1,"nextCursor":"next","totalCount":3000000000}""");
    }

    [Fact]
    public void Deserialize_WireShape_ReadsEveryMember()
    {
        var page = JsonSerializer.Deserialize<CursorPagedResult<int>>(
            """{"items":[4],"pageSize":5,"nextCursor":"c","totalCount":9}""", Web)!;

        page.Items.Should().Equal(4);
        page.PageSize.Should().Be(5);
        page.NextCursor.Should().Be("c");
        page.TotalCount.Should().Be(9);
    }
}
