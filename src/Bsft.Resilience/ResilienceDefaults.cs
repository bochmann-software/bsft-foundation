namespace Bsft.Resilience;

/// <summary>Constants of this package that a host reads rather than configures.</summary>
public static class ResilienceDefaults
{
    /// <summary>
    /// The <see cref="System.Exception.Data"/> key under which a stream retry attaches a failure that had
    /// to give way to an earlier one — in practice a fault raised while tearing down an enumerator that
    /// had already broken. The value is the suppressed <see cref="System.Exception"/> itself.
    /// </summary>
    /// <remarks>
    /// Published because it is a contract a host reads: a disposal that genuinely failed — a connection
    /// that would not close, a cursor that would not release — is diagnosable from the exception the
    /// caller was given, and finding it should not mean copying a literal out of this package.
    /// <para>
    /// The exception is stored rather than a rendered string so the host decides how much of it to
    /// surface. That decision matters: <see cref="System.Exception.Data"/> is a sink several logging
    /// pipelines serialize on their own, and a disposal failure from a database or network client can
    /// carry a host name or a connection detail in its message. Treat the value as diagnostic material to
    /// redact deliberately, not as something to render into a response.
    /// </para>
    /// </remarks>
    public const string SuppressedFailureDataKey = "Bsft.Resilience.SuppressedFailure";
}
