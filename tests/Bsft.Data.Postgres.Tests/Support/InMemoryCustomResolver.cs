using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Resolves the dynamic fields <c>custom.n</c> (an integer) and <c>custom.s</c> (a string) over the
/// <see cref="SemanticsRow.Custom"/> document in memory. A missing key and a stored JSON <c>null</c> are
/// both "not set", as the filter semantics define.
/// </summary>
/// <remarks>
/// The predicates read the value without checking that it is there: the compiler only asks them about
/// rows on which <see cref="Present"/> holds, which is part of what the property test proves.
/// </remarks>
public sealed class InMemoryCustomResolver : IDynamicFieldResolver<SemanticsRow>
{
    /// <inheritdoc />
    public DynamicField<SemanticsRow>? Resolve(string key) => key switch
    {
        "n" => new DynamicField<SemanticsRow>(
            r => Present(r, "n"),
            new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<SemanticsRow, bool>>>>
            {
                [FilterOperator.Eq] = value => Number(value, x => r => Number(r) == x),
                [FilterOperator.Gt] = value => Number(value, x => r => Number(r) > x),
                [FilterOperator.Lt] = value => Number(value, x => r => Number(r) < x),
                [FilterOperator.In] = value => Numbers(value, xs => r => xs.Contains(Number(r))),
            }),
        "s" => new DynamicField<SemanticsRow>(
            r => Present(r, "s"),
            new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<SemanticsRow, bool>>>>
            {
                [FilterOperator.Eq] = value => Text(value, x => r => Text(r) == x),
                [FilterOperator.In] = ((Func<JsonElement, Expression<Func<SemanticsRow, bool>>>)(value => Text(value, x => r => Text(r) == x))).ToInPredicate(),
                [FilterOperator.StartsWith] = value => Text(value, x => r => Text(r).StartsWith(x, StringComparison.Ordinal)),
            }),
        _ => null,
    };

    /// <summary>Whether the document holds a non-null value under the key.</summary>
    public static bool Present(SemanticsRow row, string key) =>
        row.Custom.RootElement.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null;

    private static int Number(SemanticsRow row) => row.Custom.RootElement.GetProperty("n").GetInt32();

    private static string Text(SemanticsRow row) => row.Custom.RootElement.GetProperty("s").GetString()!;

    private static Expression<Func<SemanticsRow, bool>> Number(JsonElement value, Func<int, Expression<Func<SemanticsRow, bool>>> build) =>
        build(value.GetInt32());

    private static Expression<Func<SemanticsRow, bool>> Numbers(JsonElement value, Func<int[], Expression<Func<SemanticsRow, bool>>> build) =>
        build([.. value.EnumerateArray().Select(element => element.GetInt32())]);

    private static Expression<Func<SemanticsRow, bool>> Text(JsonElement value, Func<string, Expression<Func<SemanticsRow, bool>>> build) =>
        build(value.GetString()!);
}
