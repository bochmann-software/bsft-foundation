using Bsft.Core;
using Bsft.Mediator.DependencyInjection;
using Bsft.Mediator.StartupValidation;
using Bsft.Mediator.Transactions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests.Transactions;

/// <summary>
/// The startup check that stops a host whose <c>[Transactional]</c> transaction would silently run on
/// the wrong <c>DbContext</c>. With more than one context registered and nothing to disambiguate, the
/// non-generic <c>DbContextOptions</c> binds to whichever was registered first; the check fails fast so
/// that ambiguity surfaces at startup rather than as an unprotected write at first dispatch.
/// </summary>
public class TransactionDbContextValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenTransactionBehaviorNotInPipeline_Succeeds()
    {
        // Two contexts and no base would be ambiguous, but with no transaction behavior in the
        // pipeline there is nothing to protect, so the check stays silent.
        var services = new ServiceCollection();
        AddContext<TransactionTestDbContext>(services);
        AddContext<SecondTransactionTestDbContext>(services);

        var validator = Validator(services);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenTwoContextsAndNoBase_FailsFast()
    {
        var services = new ServiceCollection();
        RegisterTransactionBehavior(services);
        AddContext<TransactionTestDbContext>(services);
        AddContext<SecondTransactionTestDbContext>(services);

        var validator = Validator(services);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle().Subject;
        // Named in full: two contexts of one simple name in different namespaces are two contexts.
        error.Should().Contain(typeof(TransactionTestDbContext).FullName);
        error.Should().Contain(typeof(SecondTransactionTestDbContext).FullName);
        error.Should().Contain("base DbContext");
    }

    [Fact]
    public async Task ValidateAsync_WhenTwoContextsButBaseDisambiguates_Succeeds()
    {
        var services = new ServiceCollection();
        RegisterTransactionBehavior(services);
        AddContext<TransactionTestDbContext>(services);
        AddContext<SecondTransactionTestDbContext>(services);
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TransactionTestDbContext>());

        var validator = Validator(services);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_WhenSingleContext_Succeeds()
    {
        var services = new ServiceCollection();
        RegisterTransactionBehavior(services);
        AddContext<TransactionTestDbContext>(services);

        var validator = Validator(services);

        var result = await validator.ValidateAsync();

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AddBsftMediator_RegistersTheValidator()
    {
        // Registered unconditionally, like the sibling validators; it decides for itself whether the
        // transaction behavior is in the pipeline before it reports anything.
        var services = new ServiceCollection();
        services.AddBsftMediator(_ => { });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<IStartupValidator>()
            .OfType<TransactionDbContextValidator>()
            .Should().ContainSingle();
    }

    [Fact]
    public void Constructor_WhenServicesNull_Throws()
    {
        var services = new ServiceCollection();
        var act = () => new TransactionDbContextValidator(
            null!, new PipelineBehaviorRegistrations(services), new TransactionDbContextResolver());

        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void Constructor_WhenRegistrationsNull_Throws()
    {
        var act = () => new TransactionDbContextValidator(
            new ServiceCollection(), null!, new TransactionDbContextResolver());

        act.Should().Throw<ArgumentNullException>().WithParameterName("registrations");
    }

    [Fact]
    public void Constructor_WhenResolverNull_Throws()
    {
        var services = new ServiceCollection();
        var act = () => new TransactionDbContextValidator(
            services, new PipelineBehaviorRegistrations(services), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resolver");
    }

    private static TransactionDbContextValidator Validator(IServiceCollection services) =>
        new(services, new PipelineBehaviorRegistrations(services), new TransactionDbContextResolver());

    private static void RegisterTransactionBehavior(IServiceCollection services) =>
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

    private static void AddContext<TContext>(IServiceCollection services)
        where TContext : DbContext =>
        services.AddDbContext<TContext>(options =>
            options.UseSqlite(new SqliteConnection("DataSource=:memory:")));
}
