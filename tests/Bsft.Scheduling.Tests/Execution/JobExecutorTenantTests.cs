using Bsft.Core.Tenancy;
using Bsft.Scheduling.Diagnostics;
using Bsft.Scheduling.Execution;
using Bsft.Scheduling.Tests.Fakes;
using Bsft.Scheduling.Tests.Support;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Bsft.Tests.Shared;
using NSubstitute;

namespace Bsft.Scheduling.Tests.Execution;

public class JobExecutorTenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 6, 0, 0, TimeSpan.Zero);

    private IJobExecutor CreateExecutor(
        IServiceScopeFactory? scopeFactory = null,
        ITenantScopeFactory? tenantScopes = null)
    {
        var factory = scopeFactory ?? Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new BsftSchedulingOptions());
        var telemetry = Substitute.For<IJobTelemetry>();

        if (tenantScopes is null)
        {
            return new JobExecutor(
                factory,
                new FixedTimeProvider(Now),
                options,
                telemetry,
                new JobRunResultRecorderFactory(options));
        }

        return new JobExecutor(
            factory,
            new FixedTimeProvider(Now),
            options,
            telemetry,
            new JobRunResultRecorderFactory(options),
            tenantScopes);
    }

    [Fact]
    public async Task ExecuteAsync_WithATenantScopeFactory_ResolvesJobFromTenantScope()
    {
        var recordedTenants = new List<string?>();
        var recordedAsyncTenants = new List<string>();
        var factory = Substitute.For<IServiceScopeFactory>();
        var tenantScopes = Substitute.For<ITenantScopeFactory>();
        var tenantScope = Substitute.For<ITenantScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        tenantScope.ServiceProvider.Returns(serviceProvider);
        tenantScopes.CreateScope(Arg.Any<string>())
            .Returns(x =>
            {
                recordedTenants.Add(x.Arg<string>());
                return tenantScope;
            });

        // Setup CreateScopeAsync to delegate to CreateScope (matching the default interface implementation)
        tenantScopes.CreateScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                recordedAsyncTenants.Add(x.Arg<string>());
                var scope = tenantScopes.CreateScope(x.Arg<string>());
                return new ValueTask<ITenantScope>(scope);
            });

        var job = Substitute.For<IJob>();
        serviceProvider.GetService(typeof(TestJob)).Returns(job);

        var executor = (JobExecutor)CreateExecutor(factory, tenantScopes);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        using (new BackgroundTenant("a"))
        {
            await executor.ExecuteAsync(typeof(TestJob), run);
        }

        recordedAsyncTenants.Should().Equal(["a"]);
        await job.Received(1).ExecuteAsync(Arg.Any<JobExecutionContext>(), Arg.Any<CancellationToken>());
        await tenantScope.Received(1).DisposeAsync();
        tenantScope.DidNotReceive().Dispose();
        factory.DidNotReceive().CreateScope();
    }

    [Fact]
    public async Task ExecuteAsync_WithAnAsyncOnlyTenantScopeFactory_RunsTheJobInTheTenantScopeAndDisposesItAsynchronously()
    {
        var services = new ServiceCollection();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddScoped<AsyncOnlyTenantScopeFactory.BoundTenant>();
        services.AddScoped<ResolvingJob>();
        using var provider = services.BuildServiceProvider();
        var tenantScopes = new AsyncOnlyTenantScopeFactory(provider.GetRequiredService<IServiceScopeFactory>());
        var executor = CreateExecutor(provider.GetRequiredService<IServiceScopeFactory>(), tenantScopes);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        using (new BackgroundTenant("a"))
        {
            await executor.ExecuteAsync(typeof(ResolvingJob), run);
        }

        tenantScopes.BoundFor.Should().Equal("a");
        tenantScopes.AsyncDisposals.Should().Be(1);
        ResolvingJob.Last.Should().NotBeNull();
        ResolvingJob.Last!.BoundTenant.Should().Be("a");
        ResolvingJob.Last.Resource.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithoutATenantScopeFactory_DisposesThePlainScopeAsynchronously()
    {
        var services = new ServiceCollection();
        services.AddScoped<AsyncOnlyDisposable>();
        services.AddScoped<AsyncOnlyTenantScopeFactory.BoundTenant>();
        services.AddScoped<ResolvingJob>();
        using var provider = services.BuildServiceProvider();
        var executor = CreateExecutor(provider.GetRequiredService<IServiceScopeFactory>());
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        // The job's scope holds a service that is only IAsyncDisposable: a scope disposed synchronously throws.
        using (new BackgroundTenant("a"))
        {
            await executor.ExecuteAsync(typeof(ResolvingJob), run);
        }

        ResolvingJob.Last.Should().NotBeNull();
        ResolvingJob.Last!.BoundTenant.Should().BeNull();
        ResolvingJob.Last.Resource.IsDisposed.Should().BeTrue();
    }

    /// <summary>A job holding a service that is only <see cref="IAsyncDisposable"/>.</summary>
    private sealed class ResolvingJob(AsyncOnlyDisposable resource, AsyncOnlyTenantScopeFactory.BoundTenant bound) : IJob
    {
        // The tests of one class run one after another, so the last run is the test's own.
        private static ResolvingJob? _last;

        public static ResolvingJob? Last => Volatile.Read(ref _last);

        public AsyncOnlyDisposable Resource => resource;

        public string? BoundTenant => bound.TenantId;

        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
        {
            Volatile.Write(ref _last, this);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithATenantScopeFactoryButNoCurrentTenant_UsesAPlainScope()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        factory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);
        serviceProvider.GetService(typeof(TestJob)).Returns(Substitute.For<IJob>());
        var tenantScopes = Substitute.For<ITenantScopeFactory>();

        var executor = CreateExecutor(factory, tenantScopes);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        await executor.ExecuteAsync(typeof(TestJob), run);

        tenantScopes.DidNotReceiveWithAnyArgs().CreateScope(default!);
        factory.Received(1).CreateScope();
    }

    [Fact]
    public async Task ExecuteAsync_WithACurrentTenantButNoTenantScopeFactory_UsesAPlainScope()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        factory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        var job = Substitute.For<IJob>();
        serviceProvider.GetService(typeof(TestJob)).Returns(job);

        var executor = CreateExecutor(factory);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        using (new BackgroundTenant("a"))
        {
            await executor.ExecuteAsync(typeof(TestJob), run);
        }

        factory.Received(1).CreateScope();
        await job.Received(1).ExecuteAsync(Arg.Any<JobExecutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_WithNullTenantScopeFactory_Throws()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new BsftSchedulingOptions());
        var telemetry = Substitute.For<IJobTelemetry>();

        var act = () => new JobExecutor(
            scopeFactory,
            new FixedTimeProvider(Now),
            options,
            telemetry,
            new JobRunResultRecorderFactory(options),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantScopes");
    }

    /// <summary>
    /// With a factory implementing CreateScopeAsync, the executor uses CreateScopeAsync (count 1)
    /// and not CreateScope (count 0), and the token passed CanBeCanceled is true.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithAsyncTenantScopeFactory_UsesCreateScopeAsyncNotCreateScope()
    {
        var recordedTokenCanBeCanceled = false;
        var createScopeAsyncCalls = 0;
        var factory = Substitute.For<IServiceScopeFactory>();
        var tenantScopes = Substitute.For<ITenantScopeFactory>();
        var tenantScope = Substitute.For<ITenantScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        tenantScope.ServiceProvider.Returns(serviceProvider);

        // Track whether CreateScopeAsync is called and the token's CanBeCanceled property
        tenantScopes.CreateScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                createScopeAsyncCalls++;
                recordedTokenCanBeCanceled = x.Arg<CancellationToken>().CanBeCanceled;
                return new ValueTask<ITenantScope>(tenantScope);
            });

        var job = Substitute.For<IJob>();
        serviceProvider.GetService(typeof(TestJob)).Returns(job);

        var executor = (JobExecutor)CreateExecutor(factory, tenantScopes);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        using (new BackgroundTenant("a"))
        {
            await executor.ExecuteAsync(typeof(TestJob), run);
        }

        createScopeAsyncCalls.Should().Be(1);
        tenantScopes.DidNotReceive().CreateScope(Arg.Any<string>());
        recordedTokenCanBeCanceled.Should().BeTrue();
    }

    /// <summary>
    /// When CreateScopeAsync throws OperationCanceledException, the job is not executed.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WhenCreateScopeAsyncThrowsOperationCanceledException_DoesNotExecuteJob()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        var tenantScopes = Substitute.For<ITenantScopeFactory>();
        var job = Substitute.For<IJob>();

        tenantScopes.CreateScopeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<ITenantScope>(Task.FromException<ITenantScope>(new OperationCanceledException())));

        var executor = CreateExecutor(factory, tenantScopes);
        var run = new JobRun { Id = 1, JobName = "Test", DueAt = Now, AttemptCount = 0, LeaseExpiresAt = Now.AddHours(1) };

        using (new BackgroundTenant("a"))
        {
            var act = async () => await executor.ExecuteAsync(typeof(TestJob), run);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        await job.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default!);
    }

    private class TestJob : IJob
    {
        public Task ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
