namespace Bsft.Hosting.Cors;

/// <summary>Carries the options of the last <c>AddBsftCors</c> call to the policy, the evaluator and the middleware.</summary>
/// <remarks>
/// Every call replaces the registration of this type, so the last call wins for the built-in origin
/// decision and for the policy name <c>UseBsftCors</c> applies, which are read from this one instance.
/// A policy registered by an earlier call under another name keeps deciding by its own call's options.
/// A host that configures <see cref="BsftCorsOptions"/> through the options pipeline afterwards changes
/// none of them.
/// </remarks>
internal sealed class BsftCorsRegistration
{
    /// <summary>Creates the registration of one call.</summary>
    /// <param name="options">The options that call bound and configured.</param>
    public BsftCorsRegistration(BsftCorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
    }

    /// <summary>Gets the options the policy, the evaluator and the middleware share.</summary>
    public BsftCorsOptions Options { get; }
}
