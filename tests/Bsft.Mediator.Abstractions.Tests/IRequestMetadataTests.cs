using Bsft.Mediator.Abstractions.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Mediator.Abstractions.Tests;

/// <summary>
/// The default answer to <see cref="IRequestMetadata.IsCommand"/> for an implementation that predates
/// the member: read off the request type.
/// </summary>
public sealed class IRequestMetadataTests
{
    private sealed record PlaceOrder : ICommand<int>;

    private sealed record Archive : ICommand;

    private sealed record FindOrder : IQuery<int>;

    [Theory]
    [InlineData(typeof(PlaceOrder), true)]
    [InlineData(typeof(Archive), true)]
    [InlineData(typeof(FindOrder), false)]
    public void IsCommand_WithoutAnOwnImplementation_FollowsTheRequestType(Type requestType, bool isCommand)
    {
        IRequestMetadata metadata = new TypeOnlyRequestMetadata(requestType);

        metadata.IsCommand.Should().Be(isCommand);
    }
}
