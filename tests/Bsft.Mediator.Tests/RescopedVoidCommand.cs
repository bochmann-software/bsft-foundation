namespace Bsft.Mediator.Tests;

/// <summary>A command without a result whose handler logs the scope marker it was resolved with.</summary>
public sealed record RescopedVoidCommand : ICommand;
