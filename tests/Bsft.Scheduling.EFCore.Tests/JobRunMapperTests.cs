using Bsft.Scheduling.EFCore.Tests.Support;
using FluentAssertions;
using Xunit;

namespace Bsft.Scheduling.EFCore.Tests;

public class JobRunMapperTests
{
    private readonly JobRunMapper _mapper = new();

    [Fact]
    public void TheMapperRejectsMissingArguments()
    {
        ((Action)(() => _mapper.ToEntity(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("run");
        ((Action)(() => _mapper.ToRun(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("entity");
    }
}
