namespace Bsft.Core;

/// <summary>
/// Announces that the application can delete accounts while ceremonies that would sign them in are running.
/// </summary>
/// <remarks>
/// A registration, not a behavior: the package that deletes accounts (<c>AddBsftOidcOrganizationAdmin</c>) registers one so
/// that a package that signs people in (<c>Bsft.Auth.Idp</c>) can refuse to start in an arrangement that would let a
/// ceremony that waited out a delete still earn a session. It lives here because both sides already reach this package
/// and neither may reference the other.
/// </remarks>
public interface IAccountDeletionSource
{
    /// <summary>Gets the name of the registration that makes accounts deletable, for the message of a startup failure.</summary>
    string Name { get; }
}
