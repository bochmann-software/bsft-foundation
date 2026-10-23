using System.Globalization;

namespace Bsft.Data;

/// <summary>
/// The settings of the data layer a deployment states rather than compiles in.
/// </summary>
/// <remarks>
/// The package's other two configuration surfaces are deliberately not here.
/// <see cref="Filtering.QueryFilteringConfiguration"/> carries assemblies to scan and
/// <see cref="MultiTenancy.MultiTenancyConfiguration"/> selects a resolver type; neither is a value a
/// configuration provider can supply, so both stay fluent registration builders and neither pretends
/// to be an options type.
/// </remarks>
public sealed class BsftDataOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Data";

    /// <summary>
    /// Gets where a request carries its tenant identifier. The property is get-only so that the
    /// configuration binder fills the same instance the fluent registration builder adjusts, instead
    /// of one of the two replacing the other's work.
    /// </summary>
    public MultiTenancyOptions MultiTenancy { get; } = new();

    /// <summary>
    /// Gets or sets whether the relational model of each relational context type is built as its first
    /// instance initializes, one context type at a time, instead of lazily by its first command.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// EF Core's lazy build has a race: two contexts of a new model that run their first command at
    /// the same moment can fail with "The model must be finalized and its runtime dependencies must be
    /// initialized before 'GetRelationalModel' can be used", which is what the first requests after a
    /// host starts do. Building the model when the context initializes, under one lock, closes it. Set
    /// this to <see langword="false"/> only to keep EF Core's own behaviour, for example to measure it.
    /// </remarks>
    public bool ShouldWarmRelationalModels { get; set; } = true;

    /// <summary>Throws when a configured value cannot be used, naming the member and the value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used.</exception>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(MultiTenancy.HeaderName))
            throw Invalid(nameof(MultiTenancy.HeaderName), "must not be blank", MultiTenancy.HeaderName);

        // A name that is not an HTTP field token can never match a header on a real request, so the
        // tenant would silently never resolve and every write of a tenant-scoped entity would be
        // refused with a message about a missing tenant rather than about the typo that caused it.
        if (!IsHttpFieldName(MultiTenancy.HeaderName))
        {
            throw Invalid(
                nameof(MultiTenancy.HeaderName),
                "must be a valid HTTP header name, so that a request can carry it",
                MultiTenancy.HeaderName);
        }

        if (string.IsNullOrWhiteSpace(MultiTenancy.ClaimType))
            throw Invalid(nameof(MultiTenancy.ClaimType), "must not be blank", MultiTenancy.ClaimType);
    }

    /// <summary>Checks a header name against the token grammar RFC 9110 defines for a field name.</summary>
    /// <param name="value">The candidate header name.</param>
    /// <returns><see langword="true"/> when every character is allowed in a field name.</returns>
    private static bool IsHttpFieldName(string value)
    {
        const string Punctuation = "!#$%&'*+-.^_`|~";

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && !Punctuation.Contains(character, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>Builds the one exception shape every failure above takes.</summary>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <param name="value">The offending value, named so the message is actionable.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidOperationException Invalid(string member, string problem, object value)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"{nameof(BsftDataOptions)}.{nameof(MultiTenancy)}.{member} {problem}, but it is '{value}'."));
}
