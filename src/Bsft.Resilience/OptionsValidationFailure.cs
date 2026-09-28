using System.Globalization;

namespace Bsft.Resilience;

/// <summary>The one exception shape both options types take when a configured value cannot be used.</summary>
/// <remarks>
/// Both types validate themselves imperatively and both say the same sentence about it. Spelling that
/// sentence out in each of them means a change to what an operator reads has to be made twice, and the
/// copy that is missed is the one that starts reading differently.
/// </remarks>
internal static class OptionsValidationFailure
{
    /// <summary>Builds the exception for a member whose value is out of bounds.</summary>
    /// <param name="typeName">The options type the member belongs to.</param>
    /// <param name="member">The member that cannot be used.</param>
    /// <param name="problem">What is wrong with it, as a clause completing "must …".</param>
    /// <param name="value">The offending value, named so the message is actionable.</param>
    /// <returns>The exception to throw.</returns>
    internal static InvalidOperationException For(
        string typeName, string member, string problem, object value)
        => new(string.Create(
            CultureInfo.InvariantCulture,
            $"{typeName}.{member} {problem}, but it is {value}."));
}
