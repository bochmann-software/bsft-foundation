using System.Linq.Expressions;
using FluentAssertions;
using Xunit;

namespace Bsft.Mapping.Tests;

public class MapBuilderNullGuardTests
{
    [Fact]
    public void ForMember_NullDestExpression_ThrowsArgumentNullException()
    {
        // MapBuilder.ForMember: null dest guard.
        var builder = new MapBuilder<SourceEntity, DestinationWithExtra>();

        var act = () => builder.ForMember<string>(null!, o => o.MapFrom(s => s.FirstName));

        act.Should().Throw<ArgumentNullException>().WithParameterName("destination");
    }

    [Fact]
    public void ForMember_NullOpts_ThrowsArgumentNullException()
    {
        // MapBuilder.ForMember: null opts guard.
        var builder = new MapBuilder<SourceEntity, DestinationWithExtra>();

        var act = () => builder.ForMember(d => d.FullName, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("opts");
    }

    [Fact]
    public void ForMember_OptsWithoutMapFrom_ThrowsInvalidOperationException()
    {
        // MapBuilder.ForMember: SourceExpression is null -> throws.
        var builder = new MapBuilder<SourceEntity, DestinationWithExtra>();

        var act = () => builder.ForMember(d => d.FullName, o => { /* no MapFrom called */ });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MapFrom must be called*");
    }

    [Fact]
    public void Ignore_NullDestExpression_ThrowsArgumentNullException()
    {
        // MapBuilder.Ignore: null dest guard.
        var builder = new MapBuilder<SourceEntity, DestinationWithExtra>();

        var act = () => builder.Ignore<string>(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("destination");
    }

    [Fact]
    public void ForMember_NonMemberAccessExpression_ThrowsArgumentException()
    {
        // MapBuilder.GetMemberName: non-member expression -> throws.
        var builder = new MapBuilder<SourceEntity, DestinationDto>();

        // Pass a method-call expression instead of a member access: d => d.ToString()
        var act = () => builder.ForMember(d => d.FirstName.ToUpper(), o => o.MapFrom(s => s.FirstName));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*member access*");
    }
}
