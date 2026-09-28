using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Bsft.Http.Tests;

/// <summary>
/// The list-item validator convention: a row of a paged result carries the tag its own resource
/// serves, so a client writes conditionally straight from the list. These tests pin the identity the
/// convention rests on — a list tag that merely resembled the detail tag would turn every write made
/// from a list row into a 412.
/// </summary>
public class ListItemEntityTagTests
{
    [Fact]
    public void VersionTag_OnAListRowAndOnTheDetailResponse_IsTheSameValue()
    {
        var version = Guid.NewGuid();
        var response = new DefaultHttpContext().Response;

        // What a list projection stamps on the row, and what the resource's own GET publishes.
        var listRowTag = version.ToVersionEntityTag();
        response.UseRevalidation(version.ToVersionEntityTag());

        response.Headers.ETag.ToString().Should().Be(listRowTag);
    }

    [Fact]
    public void VersionTag_TakenFromAListRow_SatisfiesTheConditionalWriteForThatVersion()
    {
        var version = Guid.NewGuid();
        var listRowTag = version.ToVersionEntityTag();

        // The point of the whole convention: the row's tag is a usable If-Match, so the read the
        // client used to have to make before every edit is not needed.
        version.ToVersionEntityTag().IsMatchedBy(listRowTag).Should().BeTrue();
    }

    [Fact]
    public void VersionTag_TakenFromAListRowThatHasSinceBeenWritten_RefusesTheWrite()
    {
        var listRowTag = Guid.NewGuid().ToVersionEntityTag();
        var versionAfterAnotherWrite = Guid.NewGuid();

        // Skipping the read must not skip the check: a row that went stale between the list and the
        // write is still a 412, exactly as it would be for a client that had read the resource.
        versionAfterAnotherWrite.ToVersionEntityTag().IsMatchedBy(listRowTag).Should().BeFalse();
    }
}
