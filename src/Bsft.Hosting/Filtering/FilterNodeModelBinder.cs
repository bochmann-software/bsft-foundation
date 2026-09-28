using Bsft.Data.Contracts.Filtering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Hosting.Filtering;

/// <summary>
/// Binds a <see cref="FilterNode"/> — the <c>Filter</c> of a <c>[FromQuery] PagedRequest</c> — from the
/// single <c>filter</c> query parameter, which the default query binder cannot do because the AST is
/// polymorphic. Parsing and the transport bounds are delegated to <see cref="IFilterQueryParser"/>;
/// a rejected filter surfaces as the parser's <c>ValidationException</c>, which the exception
/// middleware turns into the same <c>400</c> the rest of the CRUD convention uses.
/// </summary>
public sealed class FilterNodeModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        if (bindingContext is null)
            throw new ArgumentNullException(nameof(bindingContext));

        // Read the fixed `filter` key straight off the query rather than the model-name prefix: the
        // convention names one query parameter, and the query collection matches keys case-insensitively.
        var rawFilter = bindingContext.HttpContext.Request.Query[FilterQueryParser.FilterKey].ToString();

        var parser = bindingContext.HttpContext.RequestServices.GetRequiredService<IFilterQueryParser>();
        var node = parser.Parse(rawFilter);

        // Success(null) is deliberate: a request with no filter binds Filter to null, which is exactly
        // an unfiltered list. Returning None here would leave the parameter unbound and could trip
        // downstream required-value handling.
        bindingContext.Result = ModelBindingResult.Success(node);
        return Task.CompletedTask;
    }
}
