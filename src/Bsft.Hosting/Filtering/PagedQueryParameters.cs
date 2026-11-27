using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Bsft.Core;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Data.Contracts.Search;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bsft.Hosting.Filtering;

/// <summary>
/// The query string of a list endpoint, bound for a minimal-API handler into the <see cref="PagedRequest"/>
/// that <c>[FromQuery] PagedRequest</c> binds to in a controller.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs have no model binding for a complex query type, and <see cref="PagedRequest"/> lives in a
/// contracts package that cannot carry a <c>BindAsync</c> of its own. A handler therefore takes this type and
/// reads <see cref="Request"/>: <c>app.MapGet("/items", (PagedQueryParameters query, ...) => ...)</c>.
/// </para>
/// <para>
/// The keys are the ones the MVC binding reads, so one client serves both kinds of endpoint: <c>page</c>,
/// <c>pageSize</c>, <c>sort[0].field</c> and <c>sort[0].direction</c> (consecutive from index 0),
/// <c>search.group</c>, <c>search.term</c>, <c>search.mode</c> and <c>search.isOrderedByRank</c>, and the
/// JSON filter AST in <c>filter</c>, which <see cref="IFilterQueryParser"/> parses under its transport bounds.
/// Keys are matched case-insensitively, as the query collection does. Every key carries one value at most;
/// a repeated key is refused rather than joined. Without <c>pageSize</c> the page size is
/// <see cref="PagedRequest.DefaultPageSize"/>, or <see cref="BsftFilterTransportOptions.MaxPageSize"/> where the
/// host set that lower.
/// </para>
/// <para>
/// A value that cannot be used is a <see cref="ValidationException"/> naming the key, which the exception
/// middleware answers with a 400 like every other rejected input: a page below 1, a page size outside 1 to
/// <see cref="BsftFilterTransportOptions.MaxPageSize"/>, a page whose first row would lie beyond
/// <see cref="int.MaxValue"/>, a number that is no number, an unknown sort direction or search mode, a sort
/// entry without a field, sort entries that are not numbered from 0 without gaps, a repeated key and a search
/// without a group or a term. The entity's own query configuration may still refuse a field or cap a page
/// lower; this is the transport's half of the check.
/// </para>
/// <para>
/// A <c>filter</c> needs an <see cref="IFilterQueryParser"/> in the container, which the filter transport
/// registration provides. Without one the request fails with an <see cref="InvalidOperationException"/>, a
/// server error, because the endpoint was set up without the part that reads its filter.
/// </para>
/// </remarks>
public sealed partial class PagedQueryParameters : IBindableFromHttpContext<PagedQueryParameters>
{
    /// <summary>The query key of the one-based page number.</summary>
    public const string PageKey = "page";

    /// <summary>The query key of the page size.</summary>
    public const string PageSizeKey = "pageSize";

    /// <summary>
    /// The highest number of sort entries read. A sort longer than this is refused rather than truncated,
    /// because a truncated sort orders differently from the one the client asked for.
    /// </summary>
    public const int MaxSortCount = 16;

    /// <summary>Initializes a new instance of the <see cref="PagedQueryParameters"/> class.</summary>
    /// <param name="request">The bound request.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
    public PagedQueryParameters(PagedRequest request)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    /// <summary>Gets the paging, sorting, search and filter the query string asked for.</summary>
    public PagedRequest Request { get; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <exception cref="ValidationException">Thrown when a query value cannot be used.</exception>
    public static ValueTask<PagedQueryParameters?> BindAsync(HttpContext context, ParameterInfo parameter)
    {
        ArgumentNullException.ThrowIfNull(context);

        var query = context.Request.Query;
        var options = context.RequestServices.GetService<IOptions<BsftFilterTransportOptions>>()?.Value
            ?? new BsftFilterTransportOptions();

        var page = ReadInt(query, PageKey) ?? 1;
        var pageSize = ReadInt(query, PageSizeKey) ?? Math.Min(PagedRequest.DefaultPageSize, options.MaxPageSize);

        if (page < 1)
            throw Invalid(PageKey, "The page is one-based and has to be at least 1.");

        if (pageSize < 1 || pageSize > options.MaxPageSize)
            throw Invalid(PageSizeKey, $"The page size has to be between 1 and {options.MaxPageSize}.");

        if (page > int.MaxValue / pageSize)
            throw Invalid(PageKey, "The page lies beyond the last row a list can address.");

        var request = new PagedRequest(ReadFilter(context), ReadSort(query), page, pageSize)
        {
            Search = ReadSearch(query),
        };

        return ValueTask.FromResult<PagedQueryParameters?>(new PagedQueryParameters(request));
    }

    private static FilterNode? ReadFilter(HttpContext context)
    {
        var rawFilter = ReadSingle(context.Request.Query, FilterQueryParser.FilterKey);

        if (string.IsNullOrWhiteSpace(rawFilter))
            return null;

        return context.RequestServices.GetRequiredService<IFilterQueryParser>().Parse(rawFilter);
    }

    private static IReadOnlyList<SortItem>? ReadSort(IQueryCollection query)
    {
        var sort = new List<SortItem>();

        for (var index = 0; ; index++)
        {
            var fieldKey = $"sort[{index}].field";
            var directionKey = $"sort[{index}].direction";

            if (!query.ContainsKey(fieldKey) && !query.ContainsKey(directionKey))
                break;

            if (index == MaxSortCount)
                throw Invalid("sort", $"A list is sorted by at most {MaxSortCount} fields.");

            var field = ReadSingle(query, fieldKey);
            if (string.IsNullOrWhiteSpace(field))
                throw Invalid(fieldKey, "A sort entry names the field it sorts by.");

            sort.Add(new SortItem(field, ReadEnum(query, directionKey, SortDirection.Asc)));
        }

        // An entry past a gap would be dropped by the loop above, which sorts differently from the request.
        if (query.Keys.Any(key => SortIndex().Match(key) is { Success: true } match
                && (!int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= sort.Count)))
            throw Invalid("sort", "Sort entries are numbered from 0 without gaps.");

        return sort.Count == 0 ? null : sort;
    }

    private static SearchRequest? ReadSearch(IQueryCollection query)
    {
        const string groupKey = "search.group";
        const string termKey = "search.term";
        const string modeKey = "search.mode";
        const string rankKey = "search.isOrderedByRank";

        if (!query.ContainsKey(groupKey) && !query.ContainsKey(termKey)
            && !query.ContainsKey(modeKey) && !query.ContainsKey(rankKey))
            return null;

        var group = ReadSingle(query, groupKey);
        var term = ReadSingle(query, termKey);

        if (string.IsNullOrWhiteSpace(group))
            throw Invalid(groupKey, "A search names the search group it runs in.");

        if (string.IsNullOrWhiteSpace(term))
            throw Invalid(termKey, "A search names the term it looks for.");

        SearchMode? mode = query.ContainsKey(modeKey) ? ReadEnum(query, modeKey, SearchMode.Prefix) : null;

        return new SearchRequest(group, term, mode, ReadBool(query, rankKey) ?? true);
    }

    /// <summary>Reads a key that carries at most one value, or <see langword="null"/> when it is absent.</summary>
    private static string? ReadSingle(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var values))
            return null;

        if (values.Count != 1)
            throw Invalid(key, "The key is given once.");

        return values[0];
    }

    private static int? ReadInt(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var values))
            return null;

        if (values.Count != 1 || !int.TryParse(values[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            throw Invalid(key, "The value has to be one whole number.");

        return value;
    }

    private static bool? ReadBool(IQueryCollection query, string key)
    {
        if (!query.TryGetValue(key, out var values))
            return null;

        if (values.Count != 1 || !bool.TryParse(values[0], out var value))
            throw Invalid(key, "The value has to be true or false.");

        return value;
    }

    /// <summary>
    /// Reads an enum by its name only. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> accepts any
    /// number as well, including numbers no member has, which would hand an undefined value to the query.
    /// </summary>
    private static TEnum ReadEnum<TEnum>(IQueryCollection query, string key, TEnum absent)
        where TEnum : struct, Enum
    {
        if (!query.TryGetValue(key, out var values))
            return absent;

        var raw = values.Count == 1 ? values[0] : null;
        var match = Enum.GetNames<TEnum>().FirstOrDefault(name => string.Equals(name, raw, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            throw Invalid(key, $"The value has to be one of {string.Join(", ", Enum.GetNames<TEnum>())}.");

        return Enum.Parse<TEnum>(match);
    }

    private static ValidationException Invalid(string key, string message) =>
        new(new Dictionary<string, string[]> { [key] = [message] });

    [GeneratedRegex(@"^sort\[([^\]]*)\]\.(?:field|direction)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SortIndex();
}
