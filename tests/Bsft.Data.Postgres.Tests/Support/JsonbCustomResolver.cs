using System.Linq.Expressions;
using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Filtering;
using Microsoft.EntityFrameworkCore;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// Resolves the same dynamic fields as <see cref="InMemoryCustomResolver"/> as jsonb SQL: containment
/// (<c>@&gt;</c>) for <c>eq</c>, the key test (<c>?</c>) for presence, a cast of <c>-&gt;&gt;</c> for the
/// ordering operators and <c>= ANY(@p)</c> for <c>in</c> on <c>custom.n</c>. <c>custom.s</c> has no
/// <c>in</c> builder of its own, so the compiler builds it as an <c>or</c> over <c>eq</c>.
/// </summary>
public sealed class JsonbCustomResolver : IDynamicFieldResolver<SemanticsRow>
{
    private readonly bool _jsonNullIsPresent;

    /// <summary>Initializes a new instance of the <see cref="JsonbCustomResolver"/> class.</summary>
    /// <param name="jsonNullIsPresent">
    /// <see langword="true"/> only for the counter-check: a deliberately wrong presence test that counts a
    /// stored JSON <c>null</c> as a value, which the property test has to catch.
    /// </param>
    public JsonbCustomResolver(bool jsonNullIsPresent = false) => _jsonNullIsPresent = jsonNullIsPresent;

    /// <inheritdoc />
    public DynamicField<SemanticsRow>? Resolve(string key) => key switch
    {
        "n" => new DynamicField<SemanticsRow>(
            Present("n"),
            new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<SemanticsRow, bool>>>>
            {
                [FilterOperator.Eq] = value => Contains("n", value.GetInt32()),
                [FilterOperator.Gt] = value => Greater(value.GetInt32()),
                [FilterOperator.Lt] = value => Less(value.GetInt32()),
                [FilterOperator.In] = value => AnyOf([.. value.EnumerateArray().Select(element => element.GetInt32())]),
            }),
        "s" => new DynamicField<SemanticsRow>(
            Present("s"),
            new Dictionary<FilterOperator, Func<JsonElement, Expression<Func<SemanticsRow, bool>>>>
            {
                [FilterOperator.Eq] = value => Contains("s", value.GetString()!),
                [FilterOperator.In] = ((Func<JsonElement, Expression<Func<SemanticsRow, bool>>>)(value => Contains("s", value.GetString()!))).ToInPredicate(),
                [FilterOperator.StartsWith] = value => StartsWith(value.GetString()!),
            }),
        _ => null,
    };

    private Expression<Func<SemanticsRow, bool>> Present(string key)
    {
        if (_jsonNullIsPresent)
            return r => EF.Functions.JsonExists(r.Custom, key);

        var storedNull = JsonSerializer.Serialize(new Dictionary<string, object?> { [key] = null });
        return r => EF.Functions.JsonExists(r.Custom, key) && !EF.Functions.JsonContains(r.Custom, storedNull);
    }

    private static Expression<Func<SemanticsRow, bool>> Contains(string key, object value)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object> { [key] = value });
        return r => EF.Functions.JsonContains(r.Custom, json);
    }

    private static Expression<Func<SemanticsRow, bool>> Greater(int x) =>
        r => r.Custom.RootElement.GetProperty("n").GetInt32() > x;

    private static Expression<Func<SemanticsRow, bool>> Less(int x) =>
        r => r.Custom.RootElement.GetProperty("n").GetInt32() < x;

    private static Expression<Func<SemanticsRow, bool>> AnyOf(int[] xs) =>
        r => xs.Contains(r.Custom.RootElement.GetProperty("n").GetInt32());

    private static Expression<Func<SemanticsRow, bool>> StartsWith(string x) =>
        r => r.Custom.RootElement.GetProperty("s").GetString()!.StartsWith(x);
}
