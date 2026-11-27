namespace Bsft.Mediator.Tests;

/// <summary>A command whose handler reports the scope marker it was resolved with.</summary>
public sealed record RescopedCommand : ICommand<string>;
