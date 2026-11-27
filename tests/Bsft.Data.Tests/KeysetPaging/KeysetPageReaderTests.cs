using Bsft.Core;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.KeysetPaging;
using Bsft.Data.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Bsft.Data.Tests.KeysetPaging;

/// <summary>
/// The refusals of the reader. Each runs on a LINQ-to-objects query, which EF Core's asynchronous
/// operators cannot execute: a refusal that arrives as itself, rather than as that failure, was raised
/// before any query ran.
/// </summary>
public sealed class KeysetPageReaderTests
{
    private static readonly KeysetOrder<MeterReading, DateTime, long> ByRecordedAt =
        new("readings.by-recorded", r => r.RecordedAt, r => r.Id);

    private readonly KeysetPageReader _reader = new(new KeysetCursorSerializer(), new KeysetQueryComposer(), Options.Create(new BsftDataOptions()));

    private readonly IQueryable<MeterReading> _notExecutable = new List<MeterReading> { new() { Id = 1 } }.AsQueryable();

    [Fact]
    public async Task ListAsync_InvalidCursor_IsRefusedBeforeAnyQueryRuns()
    {
        var act = () => _reader.ListAsync(_notExecutable, ByRecordedAt, new CursorPagedRequest("garbage"));

        var refusal = (await act.Should().ThrowAsync<ValidationException>()).Which;
        refusal.ErrorCode.Should().Be(KeysetPagingErrorCodes.InvalidCursor);
        refusal.Errors.Should().ContainKey("cursor");
    }

    [Fact]
    public async Task ListAsync_EmptyCursor_IsRefusedRatherThanReadAsTheFirstPage()
    {
        var act = () => _reader.ListAsync(_notExecutable, ByRecordedAt, new CursorPagedRequest(string.Empty));

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ListAsync_UnsupportedKeyType_IsRefusedBeforeAnyQueryRuns()
    {
        var order = new KeysetOrder<MeterReading, double, long>("readings", r => r.Value, r => r.Id);

        var act = () => _reader.ListAsync(_notExecutable, order, new CursorPagedRequest());

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Double sort key*");
    }

    [Fact]
    public async Task ListAsync_UnsupportedIdType_IsRefusedBeforeAnyQueryRuns()
    {
        var order = new KeysetOrder<MeterReading, long, double>("readings", r => r.Id, r => r.Value);

        var act = () => _reader.ListAsync(_notExecutable, order, new CursorPagedRequest());

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Double id*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task ListAsync_PageSizeBelowOne_IsRefusedAsInvalidBeforeAnyQueryRuns(int pageSize)
    {
        var act = () => _reader.ListAsync(_notExecutable, ByRecordedAt, new CursorPagedRequest(PageSize: pageSize));

        var refusal = (await act.Should().ThrowAsync<ValidationException>()).Which;
        refusal.ErrorCode.Should().Be(KeysetPagingErrorCodes.InvalidPageSize);
        refusal.Errors.Should().ContainKey("pageSize");
    }

    [Fact]
    public async Task ListAsync_NullQuery_Throws()
    {
        var act = () => _reader.ListAsync(null!, ByRecordedAt, new CursorPagedRequest());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("query");
    }

    [Fact]
    public async Task ListAsync_NullOrder_Throws()
    {
        var act = () => _reader.ListAsync<MeterReading, DateTime, long>(_notExecutable, null!, new CursorPagedRequest());

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("order");
    }

    [Fact]
    public async Task ListAsync_NullRequest_Throws()
    {
        var act = () => _reader.ListAsync(_notExecutable, ByRecordedAt, null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullSerializer_Throws()
    {
        var act = () => new KeysetPageReader(null!, new KeysetQueryComposer(), Options.Create(new BsftDataOptions()));

        act.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public void Constructor_NullComposer_Throws()
    {
        var act = () => new KeysetPageReader(new KeysetCursorSerializer(), null!, Options.Create(new BsftDataOptions()));

        act.Should().Throw<ArgumentNullException>().WithParameterName("composer");
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new KeysetPageReader(new KeysetCursorSerializer(), new KeysetQueryComposer(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}
