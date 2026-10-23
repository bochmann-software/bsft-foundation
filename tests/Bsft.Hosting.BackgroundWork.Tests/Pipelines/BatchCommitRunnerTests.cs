using Bsft.Hosting.BackgroundWork.Pipelines;
using Bsft.Hosting.BackgroundWork.Tests.Pipelines.Fakes;
using FluentAssertions;
using Xunit;

namespace Bsft.Hosting.BackgroundWork.Tests.Pipelines;

public sealed class BatchCommitRunnerTests
{
    [Fact]
    public void BatchCommitRunner_WhenACollaboratorIsNull_Throws()
    {
        var committer = new RecordingBatchCommitter();

        var noCommitter = () => new BatchCommitRunner<SampleItem>(null!, new OperationRacer(TimeProvider.System));
        var noRacer = () => new BatchCommitRunner<SampleItem>(committer, null!);

        noCommitter.Should().Throw<ArgumentNullException>();
        noRacer.Should().Throw<ArgumentNullException>();
    }
}
