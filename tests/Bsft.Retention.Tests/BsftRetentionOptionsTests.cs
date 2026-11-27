using Bsft.Retention.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Retention.Tests;

public sealed class BsftRetentionOptionsTests
{
    [Fact]
    public void NameFor_PlainPass_IsTheFullName()
    {
        BsftRetentionOptions.NameFor<RecordingRetentionPass>()
            .Should().Be("Bsft.Retention.Tests.Fakes.RecordingRetentionPass");
    }

    [Fact]
    public void NameFor_ConstructedGenericPass_IsWrittenTheWayCSharpWritesIt()
    {
        // The runtime's full name would carry the assembly-qualified argument, version included.
        BsftRetentionOptions.NameFor<GenericRetentionPass<List<int>>>()
            .Should().Be("Bsft.Retention.Tests.Fakes.GenericRetentionPass<System.Collections.Generic.List<System.Int32>>");
    }

    [Fact]
    public void NameFor_PassNestedInAGenericType_DropsEveryArity()
    {
        BsftRetentionOptions.NameFor<GenericOuterType<string>.NestedRetentionPass<int>>()
            .Should().Be("Bsft.Retention.Tests.Fakes.GenericOuterType+NestedRetentionPass<System.String,System.Int32>");
    }
}
