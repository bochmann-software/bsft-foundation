using System.Linq.Expressions;
using Bsft.Data.Search;
using FluentAssertions;
using Xunit;

namespace Bsft.Data.Tests.Search;

/// <summary>
/// The box exists so the term reaches SQL as a parameter. What that needs is a member access on a
/// captured instance, which is what a provider builds over this type.
/// </summary>
public class SearchTermTests
{
    [Fact]
    public void Value_IsWhatTheTermWasBuiltFrom()
        => new SearchTerm("erfahrener:*").Value.Should().Be("erfahrener:*");

    [Fact]
    public void AMemberAccessOverIt_ReadsTheTerm()
    {
        // This is the shape a provider puts into the query: a property on a constant instance, which
        // Entity Framework Core lifts into a parameter instead of writing into the SQL text.
        var term = new SearchTerm("schweisser");

        var access = Expression.Property(Expression.Constant(term), nameof(SearchTerm.Value));

        Expression.Lambda<Func<string>>(access).Compile()().Should().Be("schweisser");
    }
}
