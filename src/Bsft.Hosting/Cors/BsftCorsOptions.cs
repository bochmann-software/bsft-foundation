namespace Bsft.Hosting.Cors;

/// <summary>
/// Configuration options for BSFT CORS policy.
/// </summary>
/// <remarks>
/// The settable members carry the policy and the fluent methods (<c>AllowOrigins</c>, <c>AllowCredentials</c>,
/// <c>AllowAny</c>, <c>ExposeHeaders</c>, <c>AllowOriginsWhere</c>) are a way of writing them, so a host may
/// configure this type either way: <c>AllowOrigins("https://app.example")</c> and
/// <c>Origins = { "https://app.example" }</c> leave the same state behind, and the second is what the
/// configuration binder does when the section is bound. Every member binds from configuration except
/// <see cref="OriginPredicate"/>, which is a delegate and can only be set in code.
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
    /// <remarks>
    /// IsAnyOriginAllowed bypasses <see cref="IBsftCorsOriginEvaluator"/>: the policy answers <c>*</c> without
    /// consulting it, so a host evaluator is ignored while this is set. A <c>*</c> entry that is the only entry of
    /// <see cref="Origins"/> does the same, because ASP.NET Core then allows every origin before any predicate or
    /// evaluator is asked; next to other entries it is an ordinary entry.
    /// </remarks>
    public bool IsAnyOriginAllowed { get; set; }

    /// <summary>
    /// Gets the response headers a browser may read from a cross-origin response (the
    /// <c>Access-Control-Expose-Headers</c> list), for example <c>ETag</c> or <c>Location</c>. Empty by
    /// default, which exposes nothing beyond the CORS-safelisted headers.
    /// </summary>
    public IList<string> ExposedHeaders { get; } = [];

    /// <summary>
    /// Gets or sets an additional origin decision, set in code and never bound from configuration. An
    /// origin is allowed when it matches <see cref="Origins"/> or when this predicate returns
    /// <see langword="true"/>. <see langword="null"/> by default, which leaves <see cref="Origins"/> alone
    /// in charge.
    /// </summary>
    /// <remarks>
    /// The predicate combines with <see cref="AreCredentialsAllowed"/> freely: ASP.NET Core only forbids
    /// credentials together with a wildcard origin, and a predicate answers per origin, so the allowed
    /// origin is echoed back and credentials stay possible. <see cref="IsAnyOriginAllowed"/> wins over the
    /// predicate, because it allows every origin already. The same decision is available outside CORS
    /// through <see cref="IBsftCorsOriginEvaluator"/>.
    /// </remarks>
    public Func<string, bool>? OriginPredicate { get; set; }

    /// <summary>
    /// Gets or sets the name the CORS policy is registered under and <c>UseBsftCors</c> applies. Defaults
    /// to <see cref="HostingDefaults.CorsPolicyName"/>.
    /// </summary>
    public string PolicyName { get; set; } = HostingDefaults.CorsPolicyName;

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
    /// Specifies response headers a browser may read from a cross-origin response.
    /// </summary>
    /// <param name="headers">One or more header names to expose.</param>
    /// <returns>The current options instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="headers"/> is null.</exception>
    public BsftCorsOptions ExposeHeaders(params string[] headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var header in headers)
            ExposedHeaders.Add(header);

        return this;
    }

    /// <summary>
    /// Allows every origin the predicate accepts, in addition to <see cref="Origins"/>.
    /// </summary>
    /// <param name="predicate">The origin decision.</param>
    /// <returns>The current options instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="predicate"/> is null.</exception>
    public BsftCorsOptions AllowOriginsWhere(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        OriginPredicate = predicate;
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when an origin or an exposed header is null, empty or whitespace, or the policy name is.
    /// </exception>
    /// <remarks>
    /// Only blank entries are rejected, and only because they cannot mean anything: a blank origin never
    /// matches a request, so it is a typo or an empty configuration key rather than a policy. The shape of
    /// a non-blank origin is left to ASP.NET Core's own policy builder, which is the component that has to
    /// match it.
    /// </remarks>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(PolicyName))
        {
            throw new InvalidOperationException(
                $"{nameof(BsftCorsOptions)}.{nameof(PolicyName)} must not be empty or whitespace.");
        }

        for (var i = 0; i < ExposedHeaders.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(ExposedHeaders[i]))
            {
                throw new InvalidOperationException(
                    $"{nameof(BsftCorsOptions)}.{nameof(ExposedHeaders)}[{i}] must not be empty or whitespace.");
            }
        }

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
