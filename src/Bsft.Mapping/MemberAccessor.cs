namespace Bsft.Mapping;

/// <summary>
/// One mapped destination member with the compiled calls that read its value from a source
/// instance and, where the destination allows it, write the value to a destination instance.
/// </summary>
/// <param name="Name">The destination member's name.</param>
/// <param name="Read">Reads the mapped value from a source instance.</param>
/// <param name="Write">
/// Writes the value to a destination instance, or <see langword="null"/> when the destination does
/// not let the mapper assign this member.
/// </param>
internal sealed record MemberAccessor(
    string Name,
    Func<object, object?> Read,
    Action<object, object?>? Write);
