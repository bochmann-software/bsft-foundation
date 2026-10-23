using Bsft.Hosting.BackgroundWork;
using FluentAssertions;

namespace Bsft.Hosting.Tests;

/// <summary>
/// A binary compiled against an older Bsft.Hosting names the run-once types as members of Bsft.Hosting; the
/// forwards have to send that reference on to Bsft.Hosting.BackgroundWork.
/// </summary>
public sealed class BackgroundWorkTypeForwardsTests
{
    public static TheoryData<string, Type> ForwardedTypes => new()
    {
        { "Bsft.Hosting.BackgroundWork.BsftRunOnceOptions", typeof(BsftRunOnceOptions) },
        { "Bsft.Hosting.BackgroundWork.RunOnceWithRetryBackgroundServiceBase", typeof(RunOnceWithRetryBackgroundServiceBase) },
        { "Bsft.Hosting.BackgroundWork.ServiceScopeFactoryExtensions", typeof(ServiceScopeFactoryExtensions) },
    };

    [Theory]
    [MemberData(nameof(ForwardedTypes))]
    public void TypeForward_WhenTheNameIsQualifiedWithBsftHosting_ResolvesTheTypeInBsftHostingBackgroundWork(
        string fullName,
        Type expected)
    {
        var resolved = Type.GetType($"{fullName}, Bsft.Hosting", throwOnError: true);

        resolved.Should().BeSameAs(expected);
        resolved!.Assembly.GetName().Name.Should().Be("Bsft.Hosting.BackgroundWork");
    }

    [Fact]
    public void TypeForward_WhenTheHostingAssemblyIsInspected_ListsExactlyTheMovedPublicTypes()
    {
        typeof(HostingDefaults).Assembly.GetForwardedTypes().Should().BeEquivalentTo(
            new[]
            {
                typeof(BsftRunOnceOptions),
                typeof(RunOnceWithRetryBackgroundServiceBase),
                typeof(ServiceScopeFactoryExtensions),
            });
    }
}
