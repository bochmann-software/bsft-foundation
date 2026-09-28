using Bsft.Core;

namespace Bsft.Sample.Inventory;

/// <summary>
/// Field-level validation for the two commands that write a product. Both carry the same editable
/// state, so both are checked against the same rules from one place — a create that is accepted and
/// an update of the same values that is rejected would be a contradiction, not a feature.
/// </summary>
/// <remarks>
/// The handlers call this validator themselves rather than relying on
/// <c>ValidationBehavior&lt;,&gt;</c>. The dispatcher resolves behaviors — and therefore the
/// <see cref="IValidator{T}"/> instances a behavior asks for — by the <em>static</em> type it
/// dispatches under (<c>ICommand&lt;TResult&gt;</c>), never by the concrete command type, so a
/// validator registered as <c>IValidator&lt;UpdateProductCommand&gt;</c> is never reached from the
/// pipeline. See the "Which behaviors apply to a request" section of <c>docs/mediator.md</c>.
/// </remarks>
public class ProductCommandValidator : IValidator<CreateProductCommand>, IValidator<UpdateProductCommand>
{
    private const int MaxNameLength = 200;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(CreateProductCommand instance, CancellationToken cancellationToken = default)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        return Task.FromResult(Validate(instance.Name, instance.StockQuantity, instance.Price));
    }

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(UpdateProductCommand instance, CancellationToken cancellationToken = default)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        return Task.FromResult(Validate(instance.Name, instance.StockQuantity, instance.Price));
    }

    /// <summary>
    /// The shared rules. The error keys are taken from <see cref="CreateProductCommand"/> for both
    /// commands: the two name their editable fields identically — which is what lets one validator
    /// serve both — so the keys a client sees are the same either way.
    /// </summary>
    private static ValidationResult Validate(string name, int stockQuantity, decimal price)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(name))
            errors[nameof(CreateProductCommand.Name)] = ["The product name is required."];
        else if (name.Length > MaxNameLength)
            errors[nameof(CreateProductCommand.Name)] =
                [$"The product name must not exceed {MaxNameLength} characters."];

        if (stockQuantity < 0)
            errors[nameof(CreateProductCommand.StockQuantity)] = ["The stock quantity must not be negative."];

        if (price <= 0)
            errors[nameof(CreateProductCommand.Price)] = ["The price must be greater than zero."];

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors);
    }
}
