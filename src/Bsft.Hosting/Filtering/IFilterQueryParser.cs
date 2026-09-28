using Bsft.Data.Contracts.Filtering;

namespace Bsft.Hosting.Filtering;

/// <summary>
/// Parses the CRUD list convention's <c>filter</c> query value — a JSON-serialized filter AST — into
/// a <see cref="FilterNode"/>, enforcing the transport bounds along the way.
/// </summary>
public interface IFilterQueryParser
{
    /// <summary>
    /// Parses the raw <c>filter</c> query value into a filter AST.
    /// </summary>
    /// <param name="rawFilter">
    /// The raw, url-decoded <c>filter</c> query value, or <see langword="null"/>/empty when the caller
    /// sent no filter.
    /// </param>
    /// <returns>
    /// The parsed filter AST, or <see langword="null"/> when <paramref name="rawFilter"/> is
    /// <see langword="null"/> or whitespace — a request with no filter is not a failed one.
    /// </returns>
    /// <exception cref="Bsft.Core.ValidationException">
    /// Thrown when the value is too long, is not valid JSON, or describes an AST that exceeds the
    /// configured depth or node-count bounds. The message is client-safe and the middleware maps it to
    /// a <c>400</c>.
    /// </exception>
    FilterNode? Parse(string? rawFilter);
}
