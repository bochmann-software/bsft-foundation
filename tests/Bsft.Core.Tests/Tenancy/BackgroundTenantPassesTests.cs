using FluentAssertions;
using Xunit;
using Bsft.Core.Polling;
using Bsft.Core.Tenancy;

namespace Bsft.Core.Tests.Tenancy;

public class BackgroundTenantPassesTests
{
    [Fact]
    public void PerTenant_WithNullSource_ReturnsSameDelegateReference()
    {
        Func<CancellationToken, Task<PollPassOutcome>> pass = _ => Task.FromResult(PollPassOutcome.WaitForInterval);

        var result = BackgroundTenantPasses.PerTenant(null, pass);

        result.Should().BeSameAs(pass);
    }

    [Fact]
    public void PerTenant_WithSource_ReturnsDifferentDelegate()
    {
        var source = new FakeBackgroundTenantSource(["t1"]);
        Func<CancellationToken, Task<PollPassOutcome>> pass = _ => Task.FromResult(PollPassOutcome.WaitForInterval);

        var result = BackgroundTenantPasses.PerTenant(source, pass);

        result.Should().NotBeSameAs(pass);
    }

    [Fact]
    public void PerTenant_WithNullPass_ThrowsArgumentNullException()
    {
        var source = new FakeBackgroundTenantSource(["t1"]);

        var act = () => BackgroundTenantPasses.PerTenant(source, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PerTenant_WithSingleTenant_RunsPassAndSetsBackgroundTenant()
    {
        var passedTenant = "";
        var source = new FakeBackgroundTenantSource(["t1"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            passedTenant = BackgroundTenant.Current ?? "";
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        await wrapped(CancellationToken.None);

        passedTenant.Should().Be("t1");
    }

    [Fact]
    public async Task PerTenant_AfterPass_CurrentIsNullAgain()
    {
        var source = new FakeBackgroundTenantSource(["t1"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
            Task.FromResult(PollPassOutcome.WaitForInterval));

        BackgroundTenant.Current.Should().BeNull();
        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        await wrapped(CancellationToken.None);
        BackgroundTenant.Current.Should().BeNull();
    }

    [Fact]
    public async Task PerTenant_WithMultipleTenants_RunsSequentiallyInOrder()
    {
        var order = new List<string>();
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            order.Add(BackgroundTenant.Current!);
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        await wrapped(CancellationToken.None);

        order.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task PerTenant_OutcomeWaitForInterval_WhenAllTenantsReturnWait()
    {
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
            Task.FromResult(PollPassOutcome.WaitForInterval));

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        var outcome = await wrapped(CancellationToken.None);

        outcome.Should().Be(PollPassOutcome.WaitForInterval);
    }

    [Fact]
    public async Task PerTenant_OutcomePollAgain_WhenAnyTenantReturnsPollAgain()
    {
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var callCount = 0;
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            callCount++;
            return Task.FromResult(callCount == 2 ? PollPassOutcome.PollAgain : PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        var outcome = await wrapped(CancellationToken.None);

        outcome.Should().Be(PollPassOutcome.PollAgain);
    }

    [Fact]
    public async Task PerTenant_EmptyTenantList_DoesNotRunPass()
    {
        var passRan = false;
        var source = new FakeBackgroundTenantSource([]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            passRan = true;
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        var outcome = await wrapped(CancellationToken.None);

        passRan.Should().BeFalse();
        outcome.Should().Be(PollPassOutcome.WaitForInterval);
    }

    [Fact]
    public async Task PerTenant_OnePassThrows_OtherTenantsStillRun()
    {
        var order = new List<string>();
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            order.Add(BackgroundTenant.Current!);
            if (BackgroundTenant.Current == "b")
                throw new InvalidOperationException("Failed for b");
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        var act = () => wrapped(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Failed for b");
        order.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task PerTenant_OnePassThrows_RethrowsSameInstanceNotWrapped()
    {
        var expectedException = new InvalidOperationException("Expected");
        var source = new FakeBackgroundTenantSource(["a", "b"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            if (BackgroundTenant.Current == "b")
                throw expectedException;
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => wrapped(CancellationToken.None));
        ex.Should().BeSameAs(expectedException);
    }

    [Fact]
    public async Task PerTenant_TwoTenantsThrow_RethrowsAggregateException()
    {
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var exceptions = new List<Exception>();
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            if (BackgroundTenant.Current is "b" or "c")
            {
                var ex = new InvalidOperationException($"Failed for {BackgroundTenant.Current}");
                exceptions.Add(ex);
                throw ex;
            }
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => wrapped(CancellationToken.None));
        ex.InnerExceptions.Should().HaveCount(2);
        ex.InnerExceptions[0].Should().BeSameAs(exceptions[0]);
        ex.InnerExceptions[1].Should().BeSameAs(exceptions[1]);
    }

    [Fact]
    public async Task PerTenant_CancellationTokenCancelledBeforeStart_ThrowsAndNoPassRuns()
    {
        var passRan = false;
        var source = new FakeBackgroundTenantSource(["a", "b"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            passRan = true;
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => wrapped(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        passRan.Should().BeFalse();
    }

    [Fact]
    public async Task PerTenant_PassThrowsOperationCancelledWhenTokenCancelled_RethrowsImmediately()
    {
        var order = new List<string>();
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var cts = new CancellationTokenSource();
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            order.Add(BackgroundTenant.Current!);
            if (BackgroundTenant.Current == "b")
            {
                cts.Cancel();
                throw new OperationCanceledException();
            }
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);

        var act = () => wrapped(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        order.Should().Equal("a", "b"); // c should not run
    }

    [Fact]
    public async Task PerTenant_PassThrowsOperationCancelledWhenTokenNotCancelled_CollectedAsFailure()
    {
        var order = new List<string>();
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        var pass = new Func<CancellationToken, Task<PollPassOutcome>>(ct =>
        {
            order.Add(BackgroundTenant.Current!);
            if (BackgroundTenant.Current == "b")
                throw new OperationCanceledException("Not from cancellation token");
            return Task.FromResult(PollPassOutcome.WaitForInterval);
        });

        var wrapped = BackgroundTenantPasses.PerTenant(source, pass);

        var act = () => wrapped(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>()
            .WithMessage("Not from cancellation token");
        order.Should().Equal("a", "b", "c"); // all should run
    }

    [Fact]
    public void CountPerTenantAsync_WithNullPass_ThrowsArgumentNullException()
    {
        var act = () => BackgroundTenantPasses.CountPerTenantAsync(null, null!);

        act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CountPerTenantAsync_WithNullSource_CallsPassOnceAndReturnsCount()
    {
        var passRan = false;
        var tenantDuringScan = "";
        Func<CancellationToken, Task<int>> pass = ct =>
        {
            passRan = true;
            tenantDuringScan = BackgroundTenant.Current ?? "";
            return Task.FromResult(42);
        };

        var result = await BackgroundTenantPasses.CountPerTenantAsync(null, pass);

        result.Should().Be(42);
        passRan.Should().BeTrue();
        tenantDuringScan.Should().BeEmpty();
    }

    [Fact]
    public async Task CountPerTenantAsync_WithSource_SumsCountsAcrossTenants()
    {
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        Func<CancellationToken, Task<int>> pass = ct =>
        {
            return Task.FromResult(BackgroundTenant.Current == "a" ? 10 : (BackgroundTenant.Current == "b" ? 20 : 30));
        };

        var result = await BackgroundTenantPasses.CountPerTenantAsync(source, pass);

        result.Should().Be(60);
    }

    [Fact]
    public async Task CountPerTenantAsync_WithFailure_RethrowsExceptionAndOtherTenantsStillRun()
    {
        var order = new List<string>();
        var source = new FakeBackgroundTenantSource(["a", "b", "c"]);
        Func<CancellationToken, Task<int>> pass = ct =>
        {
            order.Add(BackgroundTenant.Current!);
            if (BackgroundTenant.Current == "b")
                throw new InvalidOperationException("Failed");
            return Task.FromResult(1);
        };

        var act = () => BackgroundTenantPasses.CountPerTenantAsync(source, pass);

        await act.Should().ThrowAsync<InvalidOperationException>();
        order.Should().Equal("a", "b", "c");
    }
}
