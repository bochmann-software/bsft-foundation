using Bsft.Core;

namespace Bsft.Mediator;

/// <summary>
/// Pipeline behavior that resolves all registered <see cref="IValidator{T}"/> instances for the request type,
/// runs them, and throws a <see cref="Bsft.Core.ValidationException"/> if any validation errors are found.
/// </summary>
/// <typeparam name="TRequest">The type of the request to validate.</typeparam>
/// <typeparam name="TResponse">The type of the response.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">The collection of validators for the request type.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators ?? throw new ArgumentNullException(nameof(validators));
    }

    /// <inheritdoc />
    public async Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken cancellationToken = default)
    {
        if (!_validators.Any())
            return await next().ConfigureAwait(false);

        var allErrors = new Dictionary<string, List<string>>();

        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);

            if (result.IsValid)
                continue;

            foreach (var kvp in result.Errors)
            {
                if (!allErrors.TryGetValue(kvp.Key, out var existingErrors))
                {
                    existingErrors = [];
                    allErrors[kvp.Key] = existingErrors;
                }

                existingErrors.AddRange(kvp.Value);
            }
        }

        if (allErrors.Count > 0)
        {
            var errors = allErrors.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToArray());

            throw new Core.ValidationException(errors);
        }

        return await next().ConfigureAwait(false);
    }
}
