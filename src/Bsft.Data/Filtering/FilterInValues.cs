using System.Text.Json;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;

namespace Bsft.Data.Filtering;

/// <summary>
/// Reads the value set of an <see cref="FilterOperator.In"/> comparison and refuses one that does not
/// say exactly which values it means.
/// </summary>
/// <remarks>
/// <para>
/// Every evaluator of the filter AST reads the set through here, in memory as well as in SQL, so a set
/// is either refused everywhere or means the same everywhere. A set must be a JSON array of at least
/// one value and contain no <c>null</c>:
/// </para>
/// <list type="bullet">
/// <item><description>A <c>null</c> element is refused with <see cref="NullElementErrorCode"/>. "Not
/// set" has one canonical form, <c>{field, eq, null}</c>; a second spelling of it inside a set would
/// be read as <c>IS NULL OR = ANY</c> by one evaluator and as plain <c>= ANY</c> by another.</description></item>
/// <item><description>An empty array is refused with <see cref="EmptyErrorCode"/>: a filter that can
/// match nothing is a mistake in the request, not a question.</description></item>
/// </list>
/// <para>
/// A set is also bounded in size, by <see cref="EnsureWithinLimits(FilterNode, int, int)"/>: each set
/// holds at most a per-set number of values and all sets of one filter together at most a total, so
/// a filter can neither send one huge set nor spread the same load over many small ones. Exceeding
/// either is refused with <see cref="TooManyErrorCode"/>.
/// </para>
/// <para>
/// Every refusal is a <c>400</c> keyed on <c>filter</c>. They can be relaxed later without breaking a
/// client, which tightening could not.
/// </para>
/// </remarks>
public static class FilterInValues
{
    /// <summary>The error code of a set that contains a <c>null</c> element.</summary>
    public const string NullElementErrorCode = "FILTER_IN_NULL_ELEMENT";

    /// <summary>The error code of an empty set.</summary>
    public const string EmptyErrorCode = "FILTER_IN_EMPTY";

    /// <summary>The error code of a set, or of all sets of one filter together, over its limit.</summary>
    public const string TooManyErrorCode = "FILTER_IN_TOO_MANY";

    /// <summary>
    /// Reads the elements of an <c>in</c> comparison's value.
    /// </summary>
    /// <param name="node">The comparison, whose operator is <see cref="FilterOperator.In"/>.</param>
    /// <returns>The elements, at least one and none of them <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="node"/> is null.</exception>
    /// <exception cref="ValidationException">
    /// Thrown when the value is not an array, is empty or contains a <c>null</c> element.
    /// </exception>
    public static IReadOnlyList<JsonElement> Read(this ComparisonNode node)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));

        if (node.Value.ValueKind != JsonValueKind.Array)
            throw Invalid($"Operator 'in' on '{node.Field}' takes a JSON array of values.");

        var elements = new List<JsonElement>(node.Value.GetArrayLength());
        foreach (var element in node.Value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                throw Invalid(
                    $"The 'in' set on '{node.Field}' contains null. Ask for an unset field with {{\"field\": \"{node.Field}\", \"op\": \"eq\", \"value\": null}}, combined with 'or' where needed.",
                    NullElementErrorCode);
            }

            elements.Add(element);
        }

        if (elements.Count == 0)
        {
            throw Invalid(
                $"The 'in' set on '{node.Field}' is empty and would match nothing. Omit the filter, or ask for an unset field with {{\"field\": \"{node.Field}\", \"op\": \"eq\", \"value\": null}}.",
                EmptyErrorCode);
        }

        return elements;
    }

    /// <summary>
    /// Refuses a filter whose <c>in</c> sets are larger than the defaults,
    /// <see cref="FilterInLimits.DefaultMaxValuesPerSet"/> per set and <see cref="FilterInLimits.DefaultMaxValuesPerFilter"/> in total.
    /// </summary>
    /// <param name="root">The filter.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> is null.</exception>
    /// <exception cref="ValidationException">Thrown with <see cref="TooManyErrorCode"/> when a limit is exceeded.</exception>
    public static void EnsureWithinLimits(this FilterNode root) =>
        EnsureWithinLimits(root, FilterInLimits.DefaultMaxValuesPerSet, FilterInLimits.DefaultMaxValuesPerFilter);

    /// <summary>
    /// Refuses a filter whose <c>in</c> sets are larger than the given limits.
    /// </summary>
    /// <param name="root">The filter.</param>
    /// <param name="maxValuesPerSet">The most values one set may hold.</param>
    /// <param name="maxValuesPerFilter">The most values all sets of the filter may hold together.</param>
    /// <remarks>
    /// Walks the tree iteratively, so a deep filter cannot overflow the stack here. Only the size is
    /// checked; the shape of each set (an array, not empty, no <c>null</c>) is checked by
    /// <see cref="Read"/> when the set is compiled.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="root"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a limit is less than one.</exception>
    /// <exception cref="ValidationException">Thrown with <see cref="TooManyErrorCode"/> when a limit is exceeded.</exception>
    public static void EnsureWithinLimits(this FilterNode root, int maxValuesPerSet, int maxValuesPerFilter)
    {
        if (root is null)
            throw new ArgumentNullException(nameof(root));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxValuesPerSet, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxValuesPerFilter, 1);

        var total = 0;
        var pending = new Stack<FilterNode>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            PushChildren(node, pending);
            total += CountSet(node, maxValuesPerSet);

            if (total > maxValuesPerFilter)
            {
                throw Invalid(
                    $"The filter's 'in' sets hold more than {maxValuesPerFilter} values together; at most {maxValuesPerFilter} are allowed in one filter.",
                    TooManyErrorCode);
            }
        }
    }

    // The number of values an `in` comparison sends, refused when one set alone is over its limit; zero
    // for every other node, and for a value that is not an array (Read refuses that one).
    private static int CountSet(FilterNode node, int maxValuesPerSet)
    {
        if (node is not ComparisonNode { Op: FilterOperator.In, Value.ValueKind: JsonValueKind.Array } comparison)
            return 0;

        var count = comparison.Value.GetArrayLength();
        if (count > maxValuesPerSet)
        {
            throw Invalid(
                $"The 'in' set on '{comparison.Field}' holds {count} values; at most {maxValuesPerSet} are allowed in one set.",
                TooManyErrorCode);
        }

        return count;
    }

    private static void PushChildren(FilterNode node, Stack<FilterNode> pending)
    {
        IReadOnlyList<FilterNode> children = node switch
        {
            AndNode and => and.And,
            OrNode or => or.Or,
            _ => [],
        };

        foreach (var child in children)
            pending.Push(child);
    }

    private static ValidationException Invalid(string message) =>
        new(new Dictionary<string, string[]> { ["filter"] = [message] });

    private static ValidationException Invalid(string message, string errorCode) =>
        new(message, new Dictionary<string, string[]> { ["filter"] = [message] }, errorCode);
}
