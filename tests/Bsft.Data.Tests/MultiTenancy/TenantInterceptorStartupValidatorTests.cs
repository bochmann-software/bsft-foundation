using Bsft.Core;
using Bsft.Data.DependencyInjection;
using Bsft.Data.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Data.Tests.MultiTenancy;

/// <summary>
/// The write half of the tenant isolation is the half a host can leave out without noticing, which is
/// what this validator is for: a context registered without the save guard writes unstamped rows and
/// nothing at runtime says so.
/// </summary>
public class TenantInterceptorStartupValidatorTests
{
    [Fact]
    public void Constructor_NullArgument_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();

        var noServices = () => new TenantInterceptorStartupValidator(null!, scopes);
        var noScopes = () => new TenantInterceptorStartupValidator(services, null!);

        noServices.Should().Throw<ArgumentNullException>().WithParameterName("services");
        noScopes.Should().Throw<ArgumentNullException>().WithParameterName("scopes");
    }

    [Fact]
    public async Task ValidateAsync_NoTenantScopedContext_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PlainDbContext>(options => options.UseInMemoryDatabase("plain"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_TenantScopedContextWithTheGuard_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<GuardedTenantDbContext>((sp, options) => options
            .UseInMemoryDatabase("guarded")
            .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
        result.Message.Should().Be("Every tenant-scoped DbContext this check could read carries the tenant save guard.");
    }

    [Fact]
    public async Task ValidateAsync_OptionsRegisteredWithoutAddDbContextAndWithoutTheGuard_Fails()
    {
        // The options registration is what names a context, however it was made: only
        // DbContextOptions<TContext> itself is registered here, and it carries no guard.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddScoped(_ => new DbContextOptionsBuilder<GuardedTenantDbContext>()
            .UseInMemoryDatabase("hand-registered")
            .Options);

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(nameof(GuardedTenantDbContext));
    }

    [Fact]
    public async Task ValidateAsync_TenantScopedContextWithoutTheGuard_Fails()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("unguarded"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(GuardedTenantDbContext))
            .And.Contain("AddInterceptors");
    }

    [Fact]
    public async Task ValidateAsync_GuardOnlyRegisteredAsAnInterceptor_Fails()
    {
        // Entity Framework Core does not resolve an IInterceptor out of the application container: one
        // registered only there never runs, so accepting it would certify the very silent unstamped
        // write this check exists to prevent.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddScoped<IInterceptor, TenantSaveChangesInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, TenantSaveChangesInterceptor>();
        services.AddDbContext<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("by-di"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(IInterceptor))]
    [InlineData(typeof(ISaveChangesInterceptor))]
    public async Task AnInterceptorTheContainerKnowsAbout_NeverRuns(Type serviceType)
    {
        // The premise the test above rests on, asserted rather than assumed: this is what makes a
        // container registration an unguarded context rather than merely an unrecognized one. Both
        // service types are probed, because the branch that was removed accepted either.
        var recorder = new RecordingInterceptor();
        var services = new ServiceCollection();
        services.AddScoped(serviceType, _ => recorder);
        services.AddDbContext<PlainDbContext>(options => options.UseInMemoryDatabase($"di-{serviceType.Name}"));

        await SaveOneRowAsync(services);

        recorder.Ran.Should().BeFalse();
    }

    [Fact]
    public async Task TheSameInterceptorOnTheOptions_DoesRun()
    {
        // The control for the probe above: without it, "did not run" would also be the answer if the
        // fixture were simply never reached.
        var recorder = new RecordingInterceptor();
        var services = new ServiceCollection();
        services.AddDbContext<PlainDbContext>(options => options
            .UseInMemoryDatabase("on-the-options")
            .AddInterceptors(recorder));

        await SaveOneRowAsync(services);

        recorder.Ran.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_AContextThatConfiguresItselfWithoutTheGuard_Fails()
    {
        // The negative control for the OnConfiguring test below: without it, a context that could not
        // be read at all would pass just the same and the instance path would be unproven.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<SelfConfiguringUnguardedDbContext>(options => options.UseInMemoryDatabase("self-bare"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(SelfConfiguringUnguardedDbContext));
    }

    [Fact]
    public async Task ValidateAsync_AContextThatCannotBeConstructed_WarnsInsteadOfPassingQuietly()
    {
        // Failing such a host would be worse than the gap it closes, but so would letting "could not
        // be read" look exactly like "is fine".
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<DemandingTenantDbContext>(options => options.UseInMemoryDatabase("demanding"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain(nameof(DemandingTenantDbContext)).And.Contain("could not be read");
    }

    [Fact]
    public async Task ValidateAsync_GuardAddedInOnConfiguring_Succeeds()
    {
        // OnConfiguring runs on construction, so what it adds never reaches the registered options
        // object. A check that read only that object would fail a host that did it the documented way.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<SelfConfiguringTenantDbContext>(options => options.UseInMemoryDatabase("self"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_OptionsRegisteredForTheLifetimeOfTheApplication_Fails()
    {
        // The guard reads the tenant off a scoped context, so one built into singleton options is
        // resolved from the root and freezes on whichever tenant was current when it was built.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContextFactory<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("factory"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain(nameof(GuardedTenantDbContext))
            .And.Contain("lifetime of the application");
    }

    [Fact]
    public async Task ValidateAsync_AFailureAndAnUnreadableContext_ReportsBoth()
    {
        // The warning is the only place a context nobody could read is mentioned, so a failure
        // elsewhere must not swallow it.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("unguarded-too"));
        services.AddDbContext<DemandingTenantDbContext>(options => options.UseInMemoryDatabase("demanding-too"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain(nameof(GuardedTenantDbContext));
        result.Warnings.Should().ContainSingle().Which.Should().Contain(nameof(DemandingTenantDbContext));
    }

    [Fact]
    public async Task ValidateAsync_ATokenThatIsAlreadyCancelled_Stops()
    {
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("canceled"));
        await using var provider = services.BuildServiceProvider();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = async () => await Validator(services, provider)
            .ValidateAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_CancelledWhileReadingAContext_StopsBeforeTheNextOne()
    {
        // Reading a context runs host code, and there may be many of them: the token has to be worth
        // something between one and the next, not only before the first.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var cancelling = new CancellationTokenSource();
        services.AddSingleton(cancelling);
        services.AddDbContext<CancelingTenantDbContext>(options => options.UseInMemoryDatabase("cancelling"));
        services.AddDbContext<GuardedTenantDbContext>(options => options.UseInMemoryDatabase("never-reached"));
        await using var provider = services.BuildServiceProvider();

        var act = async () => await Validator(services, provider)
            .ValidateAsync(cancelling.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_AContextThatCancelsWhileBeingConstructed_DoesNotCallItUnreadable()
    {
        // A cancelled run is not a context nobody could read: reporting it as a warning would turn a
        // deliberate stop into a finding about the host.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        using var cancelling = new CancellationTokenSource();
        services.AddSingleton(cancelling);
        services.AddDbContext<CanceledWhileConstructedDbContext>(options => options.UseInMemoryDatabase("cancelled-ctor"));
        await using var provider = services.BuildServiceProvider();

        var act = async () => await Validator(services, provider)
            .ValidateAsync(cancelling.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_KeyedRegistrations_DoNotBreakTheCheck()
    {
        // Reading ImplementationType off a keyed descriptor throws. The scan must survive one.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddKeyedScoped<IInterceptor, TenantSaveChangesInterceptor>("audit");
        services.AddDbContext<GuardedTenantDbContext>((sp, options) => options
            .UseInMemoryDatabase("keyed")
            .AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>()));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_OptionsThatCannotBeBuilt_PassesWithAWarning()
    {
        // Building the options runs the host's own callback, which may need something that is not
        // there yet. A check that stopped such a host would be worse than the gap it closes, and the
        // warning is what keeps it from looking like a clean result.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddDbContext<GuardedTenantDbContext>(
            options => throw new InvalidOperationException("the connection string is not here yet"));

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Contain(nameof(GuardedTenantDbContext));
    }

    [Fact]
    public async Task ValidateAsync_OptionsThatResolveToNothing_Passes()
    {
        // A registration that answers with no options at all says nothing about an interceptor, so
        // there is nothing here to report either way.
        var services = new ServiceCollection();
        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());
        services.AddScoped<DbContextOptions<GuardedTenantDbContext>>(_ => null!);

        var result = await ValidateAsync(services);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AddBsftDataMultiTenancy_RegistersTheValidator()
    {
        var services = new ServiceCollection();

        services.AddBsftDataMultiTenancy(cfg => cfg.ResolveFromHeader());

        using var provider = services.BuildServiceProvider();
        provider.GetServices<IStartupValidator>()
            .Should().Contain(validator => validator is TenantInterceptorStartupValidator);
    }

    /// <summary>Builds the provider and saves one row through the registered context.</summary>
    /// <param name="services">The collection the host registered into.</param>
    private static async Task SaveOneRowAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PlainDbContext>();
        context.Add(new PlainRow { Id = 1 });

        await context.SaveChangesAsync();
    }

    /// <summary>Builds the provider and runs the validator over it.</summary>
    /// <param name="services">The collection the host registered into.</param>
    /// <returns>What the validator found.</returns>
    private static async Task<StartupValidationResult> ValidateAsync(IServiceCollection services)
    {
        await using var provider = services.BuildServiceProvider();

        return await Validator(services, provider).ValidateAsync(CancellationToken.None);
    }

    /// <summary>Builds the validator over an already-built provider.</summary>
    /// <param name="services">The collection the host registered into.</param>
    /// <param name="provider">The provider built from it.</param>
    /// <returns>The validator.</returns>
    private static TenantInterceptorStartupValidator Validator(
        IServiceCollection services, IServiceProvider provider)
        => new(services, provider.GetRequiredService<IServiceScopeFactory>());

    private sealed class PlainDbContext : DbContext
    {
        public PlainDbContext(DbContextOptions<PlainDbContext> options) : base(options)
        {
        }

        public DbSet<PlainRow> Rows => Set<PlainRow>();
    }

    private sealed class GuardedTenantDbContext : DbContext, ITenantScopedDbContext
    {
        public GuardedTenantDbContext(DbContextOptions<GuardedTenantDbContext> options) : base(options)
        {
        }

        public string? CurrentTenantId => null;
    }

    private sealed class PlainRow
    {
        public int Id { get; set; }
    }

    private sealed class RecordingInterceptor : ISaveChangesInterceptor
    {
        public bool Ran { get; private set; }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Ran = true;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SelfConfiguringUnguardedDbContext : DbContext, ITenantScopedDbContext
    {
        public SelfConfiguringUnguardedDbContext(DbContextOptions<SelfConfiguringUnguardedDbContext> options)
            : base(options)
        {
        }

        public string? CurrentTenantId => null;
    }

    private sealed class CancelingTenantDbContext : DbContext, ITenantScopedDbContext
    {
        public CancelingTenantDbContext(
            DbContextOptions<CancelingTenantDbContext> options,
            CancellationTokenSource cancelling) : base(options)
            => cancelling.Cancel();

        public string? CurrentTenantId => null;
    }

    private sealed class CanceledWhileConstructedDbContext : DbContext, ITenantScopedDbContext
    {
        public CanceledWhileConstructedDbContext(
            DbContextOptions<CanceledWhileConstructedDbContext> options,
            CancellationTokenSource cancelling) : base(options)
        {
            cancelling.Cancel();
            cancelling.Token.ThrowIfCancellationRequested();
        }

        public string? CurrentTenantId => null;
    }

    private sealed class DemandingTenantDbContext : DbContext, ITenantScopedDbContext
    {
        public DemandingTenantDbContext(DbContextOptions<DemandingTenantDbContext> options)
            : base(options)
            => throw new InvalidOperationException("the connection string is not here yet");

        public string? CurrentTenantId => null;
    }

    private sealed class SelfConfiguringTenantDbContext : DbContext, ITenantScopedDbContext
    {
        private readonly TenantSaveChangesInterceptor _guard;

        public SelfConfiguringTenantDbContext(
            DbContextOptions<SelfConfiguringTenantDbContext> options,
            TenantSaveChangesInterceptor guard) : base(options)
        {
            _guard = guard;
        }

        public string? CurrentTenantId => null;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
            => optionsBuilder.AddInterceptors(_guard);
    }
}
