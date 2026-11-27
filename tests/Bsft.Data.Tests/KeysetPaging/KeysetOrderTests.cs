using Bsft.Data.Contracts.Pagination;
using Bsft.Data.KeysetPaging;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.KeysetPaging;

public sealed class KeysetOrderTests
{
    [Fact]
    public void Constructor_ValidOrder_KeepsWhatItWasGiven()
    {
        var order = new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id, SortDirection.Desc);

        order.Listing.Should().Be("readings");
        order.Key.Body.Type.Should().Be<DateTime>();
        order.Id.Body.Type.Should().Be<long>();
        order.Direction.Should().Be(SortDirection.Desc);
    }

    [Fact]
    public void Constructor_NoDirection_IsAscending() =>
        new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id)
            .Direction.Should().Be(SortDirection.Asc);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankListing_Throws(string listing)
    {
        var act = () => new KeysetOrder<MeterReading, DateTime, long>(listing, r => r.RecordedAt, r => r.Id);

        act.Should().Throw<ArgumentException>().WithParameterName("listing");
    }

    [Fact]
    public void Constructor_NullKey_Throws()
    {
        var act = () => new KeysetOrder<MeterReading, DateTime, long>("readings", null!, r => r.Id);

        act.Should().Throw<ArgumentNullException>().WithParameterName("key");
    }

    [Fact]
    public void Constructor_NullId_Throws()
    {
        var act = () => new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("id");
    }

    [Fact]
    public void Constructor_UndefinedDirection_Throws()
    {
        var act = () => new KeysetOrder<MeterReading, DateTime, long>("readings", r => r.RecordedAt, r => r.Id, (SortDirection)7);

        act.Should().Throw<ArgumentException>().WithParameterName("direction");
    }

    [Fact]
    public void Constructor_NullableKey_Throws()
    {
        var act = () => new KeysetOrder<MeterReading, DateTime?, long>("readings", r => r.RecordedAt, r => r.Id);

        act.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Fact]
    public void Constructor_NullableId_Throws()
    {
        var act = () => new KeysetOrder<MeterReading, DateTime, long?>("readings", r => r.RecordedAt, r => r.Id);

        act.Should().Throw<ArgumentException>().WithParameterName("id");
    }

    [Fact]
    public void GetPosition_Row_ReadsKeyAndId()
    {
        var order = new KeysetOrder<MeterReading, string, long>("readings", r => r.Meter, r => r.Id);

        order.GetPosition(new MeterReading { Id = 5, Meter = "m-1" })
            .Should().BeEquivalentTo(new KeysetPosition<string, long>("m-1", 5));
    }

    [Fact]
    public void GetPosition_Null_Throws()
    {
        var order = new KeysetOrder<MeterReading, string, long>("readings", r => r.Meter, r => r.Id);

        var act = () => order.GetPosition(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("item");
    }
}
