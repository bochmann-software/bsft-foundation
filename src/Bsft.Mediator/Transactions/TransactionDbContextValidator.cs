using Bsft.Core;
using Bsft.Mediator.StartupValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Mediator.Transactions;

/// <summary>
/// Startup check that fails fast when <see cref="TransactionBehavior{TRequest, TResponse}"/> is in
/// the pipeline and the container leaves the transaction's <c>DbContext</c> ambiguous — more than one
/// <c>DbContext</c> registered through <c>AddDbContext&lt;T&gt;()</c> with no explicit base
/// <c>DbContext</c> to disambiguate.
/// </summary>
/// <remarks>
/// The behavior falls back to the context named by the non-generic <c>DbContextOptions</c> so a plain
/// <c>AddDbContext&lt;T&gt;()</c> host gets a transaction with no extra wiring. EF Core's
/// <c>TryAdd</c> binds that non-generic <c>DbContextOptions</c> to whichever context was registered
/// first, so under several registrations the fallback would silently open and commit a transaction on
/// one context while the handler writes to another — false confidence that is worse than the honest
/// "no DbContext" warning it replaced. This pipeline surfaces that kind of ambiguity at startup, the
/// same way <c>UseDefaults()</c> fixes behavior order and the coverage check reports an unenforced
/// permission, rather than inferring silently on the first dispatch. The single-context case resolves
/// silently; an explicit base <c>DbContext</c> registration wins; only the genuinely ambiguous host
/// is stopped, and it is told to register the base <c>DbContext</c> explicitly.
/// </remarks>
public sealed class TransactionDbContextValidator : IStartupValidator
{
    private static readonly Type TransactionBehaviorType = typeof(TransactionBehavior<,>);

    private readonly IServiceCollection _services;
    private readonly PipelineBehaviorRegistrations _registrations;
    private readonly ITransactionDbContextResolver _resolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionDbContextValidator"/> class.
    /// </summary>
    /// <param name="services">The live service collection, so a <c>DbContext</c> registered after
    /// <c>AddBsftMediator</c> is seen too.</param>
    /// <param name="registrations">The behavior registrations, used to tell whether the transaction
    /// behavior is in the pipeline at all.</param>
    /// <param name="resolver">The shared resolver whose ambiguity rule the check reuses, so runtime
    /// resolution and this check agree on what "ambiguous" means.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    internal TransactionDbContextValidator(
        IServiceCollection services,
        PipelineBehaviorRegistrations registrations,
        ITransactionDbContextResolver resolver)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _registrations = registrations ?? throw new ArgumentNullException(nameof(registrations));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <summary>
    /// Fails when the transaction behavior is registered and more than one <c>DbContext</c> is
    /// registered with no explicit base to disambiguate; passes otherwise.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A <see cref="StartupValidationResult"/> that fails only on the ambiguous configuration.</returns>
    public Task<StartupValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (!_registrations.IsRegistered(TransactionBehaviorType))
            return Task.FromResult(StartupValidationResult.Success(
                "TransactionBehavior is not in the pipeline; the DbContext registration is not checked."));

        var ambiguous = _resolver.ListAmbiguousContexts(_services);
        if (ambiguous.Count == 0)
            return Task.FromResult(StartupValidationResult.Success(
                "The transaction DbContext resolves unambiguously."));

        return Task.FromResult(StartupValidationResult.Failure(BuildAmbiguityError(ambiguous)));
    }

    /// <summary>
    /// The error naming the competing contexts and the one line that disambiguates them.
    /// </summary>
    private static string BuildAmbiguityError(IReadOnlyList<Type> ambiguous)
    {
        var contextNames = string.Join(", ", ambiguous.Select(type => type.FullName ?? type.Name));

        return
            $"""
             TransactionBehavior is in the pipeline but the DbContext it should wrap is ambiguous:
             {ambiguous.Count} DbContext types are registered ({contextNames}) and none is registered
             as the base DbContext service. The non-generic DbContextOptions binds to whichever context
             was registered first, so the transaction would silently run on that one while a handler
             writing to another context stays unprotected. Register the base DbContext explicitly to
             say which context the transaction wraps, for example
             services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>()).
             """;
    }
}
