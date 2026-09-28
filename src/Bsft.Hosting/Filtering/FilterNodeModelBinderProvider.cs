using Bsft.Data.Contracts.Filtering;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bsft.Hosting.Filtering;

/// <summary>
/// Supplies the filter model binder for the <see cref="FilterNode"/> type, so binding a
/// <c>[FromQuery] PagedRequest</c> resolves its polymorphic <c>Filter</c> from the <c>filter</c> query
/// parameter. Registered ahead of the default providers, which would otherwise try — and fail — to bind
/// the abstract-by-convention AST as a complex object from query keys.
/// </summary>
public sealed class FilterNodeModelBinderProvider : IModelBinderProvider
{
    private readonly IModelBinder _binder;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterNodeModelBinderProvider"/> class.
    /// </summary>
    /// <param name="binder">The binder handed out for the <see cref="FilterNode"/> type.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="binder"/> is null.</exception>
    public FilterNodeModelBinderProvider(IModelBinder binder)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        return context.Metadata.ModelType == typeof(FilterNode) ? _binder : null;
    }
}
