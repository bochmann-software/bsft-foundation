namespace Bsft.Data.Tests.Support;

/// <summary>
/// An entity mixing a reference field, a nullable value field and a non-nullable value field, used to
/// exercise the three arms of the presence (<c>pr</c>) compiler path.
/// </summary>
internal class PresenceEntity
{
    public string? Name { get; set; }
    public int? OptionalAge { get; set; }
    public int RequiredAge { get; set; }
}
