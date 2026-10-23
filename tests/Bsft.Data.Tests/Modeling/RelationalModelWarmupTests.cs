using Bsft.Data.Modeling;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Bsft.Data.Tests.Modeling;

/// <summary>
/// The warm-up's one job: leave a model with its relational model built, also when many threads ask
/// for the same new model at once, which is what EF Core's own lazy build cannot survive.
/// </summary>
public sealed class RelationalModelWarmupTests
{
    [Fact]
    public void WithNullContextThrows()
    {
        var act = () => RelationalModelWarmup.EnsureBuilt((DbContext)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void WithNullModelThrows()
    {
        var act = () => RelationalModelWarmup.EnsureBuilt((IModel)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildsTheRelationalModelAndRetiresTheFactory()
    {
        var model = FreshModelContext.UnbuiltModel();
        RelationalModelWarmup.IsBuilt(model).Should().BeFalse("the model comes from no context");

        RelationalModelWarmup.EnsureBuilt(model);

        RelationalModelWarmup.IsBuilt(model).Should().BeTrue();
        model.FindRuntimeAnnotation(RelationalAnnotationNames.RelationalModelFactory).Should().BeNull(
            "EF Core removes the factory once the model is built, which is what opens the gap");
    }

    [Fact]
    public void LeavesABuiltModelAsItIs()
    {
        var model = FreshModelContext.UnbuiltModel();
        RelationalModelWarmup.EnsureBuilt(model);
        var built = model.GetRelationalModel();

        RelationalModelWarmup.EnsureBuilt(model);

        model.GetRelationalModel().Should().BeSameAs(built);
    }

    /// <summary>
    /// Many threads released at once onto the same new model: every one of them comes back with the
    /// one relational model, and none hits EF Core's "must be finalized" failure.
    /// </summary>
    [Fact]
    public async Task BuildsOneRelationalModelWhenManyThreadsAskAtOnce()
    {
        const int Threads = 16;
        var model = FreshModelContext.UnbuiltModel();
        using var barrier = new Barrier(Threads);

        var results = Enumerable.Range(0, Threads)
            .Select(_ => Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    RelationalModelWarmup.EnsureBuilt(model);
                    return model.GetRelationalModel();
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        var models = await Task.WhenAll(results);

        models.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void BuildsTheRuntimeModelOfARelationalContext()
    {
        using var context = new FreshModelContext(Guid.NewGuid());

        RelationalModelWarmup.EnsureBuilt(context);

        RelationalModelWarmup.IsBuilt(context.Model).Should().BeTrue();
    }

    [Fact]
    public void LeavesANonRelationalContextAlone()
    {
        using var context = new FreshModelContext(Guid.NewGuid(), relational: false);

        var act = () => RelationalModelWarmup.EnsureBuilt(context);

        act.Should().NotThrow("a provider without a relational model has nothing to build");
    }
}
