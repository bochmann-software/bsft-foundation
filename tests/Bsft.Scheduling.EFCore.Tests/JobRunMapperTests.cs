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
        ((Action)(() => _mapper.ToResult(null!)))
            .Should().Throw<ArgumentNullException>().WithParameterName("entity");
    }

    [Fact]
    public void TheResultIsStoredAsItsSchemaAndItsJsonText()
    {
        var run = new JobRun { JobName = "mail-dispatch", Result = TestRunResults.Of("mail-dispatch/v1", """{"sent":1}""") };

        var entity = _mapper.ToEntity(run);

        entity.ResultSchema.Should().Be("mail-dispatch/v1");
        entity.Result.Should().Be("""{"sent":1}""");
    }

    [Fact]
    public void NoResultIsWrittenAsNoJsonText()
    {
        _mapper.ToResultJson(null).Should().BeNull();
    }

    [Fact]
    public void ARunWithoutAResultIsStoredWithoutOne()
    {
        var entity = _mapper.ToEntity(new JobRun { JobName = "silent" });

        entity.ResultSchema.Should().BeNull();
        entity.Result.Should().BeNull();
    }

    [Fact]
    public void TheStoredResultIsReadBackOntoTheRun()
    {
        var entity = new JobRunEntity { JobName = "mail-dispatch", ResultSchema = "mail-dispatch/v1", Result = """{"sent":1}""" };

        _mapper.ToRun(entity).Result.Should().Be(TestRunResults.Of("mail-dispatch/v1", """{"sent":1}"""));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("mail-dispatch/v1", null)]
    [InlineData(null, """{"sent":1}""")]
    public void ARowMissingEitherHalfOfAResultCarriesNone(string? schema, string? json)
    {
        var entity = new JobRunEntity { JobName = "mail-dispatch", ResultSchema = schema, Result = json };

        _mapper.ToResult(entity).Should().BeNull();
    }
}
