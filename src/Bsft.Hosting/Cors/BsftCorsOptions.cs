namespace Bsft.Hosting.Cors;

/// <summary>
/// Configuration options for BSFT CORS policy.
/// </summary>
/// <remarks>
/// The three members below carry the policy and the three methods are a fluent way of writing them, so
/// a host may configure this type either way: <c>AllowOrigins("https://app.example")</c> and
/// <c>Origins = { "https://app.example" }</c> leave the same state behind, and the second is what the
/// configuration binder does when the section is bound.
/// </remarks>
public sealed class BsftCorsOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Bsft:Hosting:Cors";

    /// <summary>
    /// Gets the origins the policy allows. Empty by default, which allows no cross-origin request at all.
    /// </summary>
    public IList<string> Origins { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether credentials (cookies, authorization headers) are allowed
    /// in cross-origin requests. Ignored when <see cref="IsAnyOriginAllowed"/> is set.
    /// </summary>
    public bool AreCredentialsAllowed { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether any origin, method and header is allowed. Intended for
    /// development mode only.
    /// </summary>
    public bool IsAnyOriginAllowed { get; set; }

    /// <summary>
    /// Specifies the allowed origins for CORS requests.
    /// </summary>
    /// <param name="origins">One or more origin URLs to allow.</param>
    /// <returns>The current options instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="origins"/> is null.</exception>
    public BsftCorsOptions AllowOrigins(params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        foreach (var origin in origins)
            Origins.Add(origin);

        return this;
    }

    /// <summary>
    /// Allows credentials (cookies, authorization headers) in CORS requests.
    /// </summary>
    /// <returns>The current options instance for fluent chaining.</returns>
    public BsftCorsOptions AllowCredentials()
    {
        AreCredentialsAllowed = true;
        return this;
    }

    /// <summary>
    /// Allows any origin, method, and header. Intended for development mode only.
    /// </summary>
    /// <returns>The current options instance for fluent chaining.</returns>
    public BsftCorsOptions AllowAny()
    {
        IsAnyOriginAllowed = true;
        return this;
    }

    /// <summary>Throws when a configured origin cannot be used, naming the offending value.</summary>
    /// <exception cref="InvalidOperationException">Thrown when an origin is null, empty or whitespace.</exception>
    /// <remarks>
    /// Only blank entries are rejected, and only because they cannot mean anything: a blank origin never
    /// matches a request, so it is a typo or an empty configuration key rather than a policy. The shape of
    /// a non-blank origin is left to ASP.NET Core's own policy builder, which is the component that has to
    /// match it.
    /// </remarks>
    internal void Validate()
    {
        for (var i = 0; i < Origins.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(Origins[i]))
            {
                throw new InvalidOperationException(
                    $"{nameof(BsftCorsOptions)}.{nameof(Origins)}[{i}] must not be empty or whitespace.");
            }
        }
    }
}
