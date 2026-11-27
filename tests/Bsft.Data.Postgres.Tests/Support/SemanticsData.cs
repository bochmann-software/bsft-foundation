using System.Text.Json;
using Bsft.Data.Contracts.Filtering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>
/// The rows, the field maps and the random filters the filter-semantics tests run on.
/// </summary>
internal static class SemanticsData
{
    private static readonly int?[] Sizes = [null, 0, 1, 2, 3];
    private static readonly string?[] Labels = [null, "a", "ab", "b", "B"];
    private static readonly int[] Numbers = [0, 1, 2, 3];
    private static readonly string[] Texts = ["a", "ab", "b", "B"];

    /// <summary>Every shape of the custom document: missing keys, stored JSON nulls and values.</summary>
    private static readonly string[] Documents =
    [
        "{}",
        "{\"n\": null}",
        "{\"s\": null}",
        "{\"n\": null, \"s\": null}",
        "{\"n\": 0}",
        "{\"n\": 1}",
        "{\"n\": 3}",
        "{\"s\": \"a\"}",
        "{\"s\": \"ab\"}",
        "{\"s\": \"B\"}",
        "{\"n\": 2, \"s\": \"b\"}",
        "{\"n\": null, \"s\": \"a\"}",
        "{\"n\": 1, \"s\": null}",
    ];

    /// <summary>
    /// Builds the field map of the two declared nullable columns and the dynamic prefix <c>custom.</c>.
    /// </summary>
    /// <param name="resolver">The resolver of <c>custom.n</c> and <c>custom.s</c>.</param>
    /// <returns>The field map.</returns>
    public static QueryFieldMap<SemanticsRow> FieldMap(IDynamicFieldResolver<SemanticsRow> resolver, int? maxInValues = null)
    {
        var builder = new QueryConfigurationBuilder<SemanticsRow, SemanticsRowDto>();
        if (maxInValues is { } max)
            builder.MaxInValues(max);
        builder.Field(d => d.Size)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Neq, FilterOperator.Gt, FilterOperator.Lt, FilterOperator.In, FilterOperator.Present);
        builder.Field(d => d.Label)
            .AllowFilter(FilterOperator.Eq, FilterOperator.Neq, FilterOperator.StartsWith, FilterOperator.NotStartsWith, FilterOperator.In, FilterOperator.Present);
        builder.DynamicFields("custom.", resolver);
        return builder.BuildFieldMap();
    }

    /// <summary>
    /// Creates the table in the given database, stores one row per combination of size, label and
    /// document, and returns the rows read back, so the in-memory side sees exactly what the database holds.
    /// </summary>
    /// <param name="context">A context on the test's own database.</param>
    /// <returns>The stored rows.</returns>
    public static async Task<List<SemanticsRow>> SeedAsync(SemanticsContext context)
    {
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();

        var id = 0;
        foreach (var size in Sizes)
        {
            foreach (var label in Labels)
            {
                foreach (var document in Documents)
                    context.Rows.Add(new SemanticsRow { Id = ++id, Size = size, Label = label, Custom = JsonDocument.Parse(document) });
            }
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return await context.Rows.AsNoTracking().OrderBy(r => r.Id).ToListAsync();
    }

    /// <summary>A random filter tree of at most <paramref name="depth"/> levels of <c>and</c> and <c>or</c>.</summary>
    /// <param name="random">The seeded source.</param>
    /// <param name="depth">The levels left.</param>
    /// <returns>The filter.</returns>
    public static FilterNode RandomFilter(Random random, int depth)
    {
        var kind = depth == 0 ? 2 : random.Next(4);
        if (kind >= 2)
            return RandomLeaf(random);

        var children = Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomFilter(random, depth - 1)).ToList();
        return kind == 0 ? new AndNode { And = children } : new OrNode { Or = children };
    }

    private static FilterNode RandomLeaf(Random random)
    {
        var field = Pick(random, ["Size", "Label", "custom.n", "custom.s"]);
        var numeric = field is "Size" or "custom.n";

        if (random.Next(6) == 0)
            return new PresenceNode { Field = field };

        FilterOperator[] operators = numeric
            ? [FilterOperator.Eq, FilterOperator.Neq, FilterOperator.Gt, FilterOperator.Lt, FilterOperator.In]
            : [FilterOperator.Eq, FilterOperator.Neq, FilterOperator.StartsWith, FilterOperator.In];
        if (field == "Label")
            operators = [.. operators, FilterOperator.NotStartsWith];

        var op = Pick(random, operators);
        JsonElement value;
        if (op == FilterOperator.In)
            value = Json(Enumerable.Range(0, random.Next(1, 4)).Select(_ => RandomValue(random, numeric)).ToArray());
        else if (op is FilterOperator.Eq or FilterOperator.Neq && random.Next(4) == 0)
            value = Json(null);
        else
            value = Json(RandomValue(random, numeric));

        return new ComparisonNode { Field = field, Op = op, Value = value };
    }

    private static object RandomValue(Random random, bool numeric) =>
        numeric ? Pick(random, Numbers) : Pick(random, Texts);

    private static T Pick<T>(Random random, T[] values) => values[random.Next(values.Length)];

    private static JsonElement Json(object? value) => JsonSerializer.SerializeToElement(value);
}
