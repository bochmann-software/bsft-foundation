namespace Bsft.Mediator;

/// <summary>
/// Marker attribute indicating that the decorated handler should be executed within a database transaction.
/// Used by the <c>TransactionBehavior</c> pipeline behavior.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TransactionalAttribute : Attribute;
