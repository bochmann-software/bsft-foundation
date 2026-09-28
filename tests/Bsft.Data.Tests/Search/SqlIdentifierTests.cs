using Bsft.Data.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The three provider packages interpolate these names straight into DDL, so what this accepts is what
/// reaches the database as syntax rather than as data.
/// </summary>
public class SqlIdentifierTests
{
    [Theory]
    [InlineData("SearchVector")]
    [InlineData("search_vector")]
    [InlineData("Column1")]
    [InlineData("_leading")]
    public void Require_AName_ReturnsItUnchanged(string value)
        => SqlIdentifier.Require(value, "name").Should().Be(value);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_NothingAtAll_ThrowsArgumentException(string value)
    {
        var act = () => SqlIdentifier.Require(value, "name");

        act.Should().Throw<ArgumentException>().WithParameterName("name")
            .WithMessage("An identifier is required.*");
    }

    [Theory]
    [InlineData("drop\"table")]
    [InlineData("a;b")]
    [InlineData("has space")]
    [InlineData("a-b")]
    [InlineData("a'b")]
    public void Require_AnythingNeedingEscaping_ThrowsArgumentException(string value)
    {
        var act = () => SqlIdentifier.Require(value, "name");

        act.Should().Throw<ArgumentException>().WithParameterName("name")
            .WithMessage($"'{value}' is not a valid SQL identifier.*");
    }

    [Fact]
    public void Require_NullValue_ThrowsArgumentException()
    {
        var act = () => SqlIdentifier.Require(null!, "name");

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    [Fact]
    public void RequireColumns_Names_ReturnsThemInTheOrderTheyWereGiven()
        => SqlIdentifier.RequireColumns(["Name", "Notes"], "columns").Should().Equal("Name", "Notes");

    [Fact]
    public void RequireColumns_OneNameNeedingEscaping_ThrowsArgumentException()
    {
        var act = () => SqlIdentifier.RequireColumns(["Name", "a;b"], "columns");

        act.Should().Throw<ArgumentException>().WithParameterName("columns")
            .WithMessage("'a;b' is not a valid SQL identifier.*");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequireColumns_NoColumnAtAll_ThrowsArgumentException(bool asNull)
    {
        // An index over no column is a statement no database accepts, and a null list is the same
        // answer arriving by a different route.
        var act = () => SqlIdentifier.RequireColumns(asNull ? null! : [], "columns");

        act.Should().Throw<ArgumentException>().WithParameterName("columns")
            .WithMessage("At least one column must be indexed.*");
    }
}
