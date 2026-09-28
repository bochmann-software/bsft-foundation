namespace Bsft.Core;

/// <summary>
/// Represents a void return type for commands that do not produce a result.
/// Implements the singleton pattern through <see cref="Value"/>.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    private const string ScreamingCaseObsoleteMessage =
        "Use Unit.Value and Unit.Task. The screaming-case names are kept so that existing code keeps "
        + "compiling and will be removed in a future major version.";

    /// <summary>
    /// Gets the singleton instance of <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = default;

    /// <summary>
    /// Returns a completed task containing the singleton <see cref="Unit"/> value.
    /// </summary>
    public static readonly System.Threading.Tasks.Task<Unit> Task =
        System.Threading.Tasks.Task.FromResult(Value);

    /// <summary>
    /// Gets the singleton instance of <see cref="Unit"/>, under the name it first shipped with.
    /// </summary>
    [Obsolete(ScreamingCaseObsoleteMessage)]
    public static readonly Unit VALUE = default;

    /// <summary>
    /// Returns a completed task containing the singleton <see cref="Unit"/> value, under the name it
    /// first shipped with.
    /// </summary>
    [Obsolete(ScreamingCaseObsoleteMessage)]
    public static readonly System.Threading.Tasks.Task<Unit> TASK = Task;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <inheritdoc />
    public override string ToString() => "()";

    // The operands carry a leading underscore because the operator signature is fixed and neither
    // operand is read. That is the form the unused-parameter rule asks for, and unlike interface
    // implementations and overrides, operator overloads are not exempt from it. Renaming them to
    // left/right reads better but reopens the finding, so it has been tried and reverted twice.

    /// <summary>
    /// Determines whether two <see cref="Unit"/> instances are equal.
    /// All instances are equal, so the operands are not inspected.
    /// </summary>
    /// <param name="_left">The left operand; ignored because every instance is equal.</param>
    /// <param name="_right">The right operand; ignored because every instance is equal.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool operator ==(Unit _left, Unit _right) => true;

    /// <summary>
    /// Determines whether two <see cref="Unit"/> instances are not equal.
    /// All instances are equal, so the operands are not inspected.
    /// </summary>
    /// <param name="_left">The left operand; ignored because every instance is equal.</param>
    /// <param name="_right">The right operand; ignored because every instance is equal.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public static bool operator !=(Unit _left, Unit _right) => false;

    /// <summary>Determines whether one <see cref="Unit"/> sorts before another.</summary>
    /// <param name="_left">The left operand; ignored because every instance compares equal.</param>
    /// <param name="_right">The right operand; ignored because every instance compares equal.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public static bool operator <(Unit _left, Unit _right) => false;

    /// <summary>Determines whether one <see cref="Unit"/> sorts after another.</summary>
    /// <param name="_left">The left operand; ignored because every instance compares equal.</param>
    /// <param name="_right">The right operand; ignored because every instance compares equal.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    public static bool operator >(Unit _left, Unit _right) => false;

    /// <summary>Determines whether one <see cref="Unit"/> sorts before another or equals it.</summary>
    /// <param name="_left">The left operand; ignored because every instance compares equal.</param>
    /// <param name="_right">The right operand; ignored because every instance compares equal.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool operator <=(Unit _left, Unit _right) => true;

    /// <summary>Determines whether one <see cref="Unit"/> sorts after another or equals it.</summary>
    /// <param name="_left">The left operand; ignored because every instance compares equal.</param>
    /// <param name="_right">The right operand; ignored because every instance compares equal.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public static bool operator >=(Unit _left, Unit _right) => true;
}
