namespace Bsft.Mediator.Tests.Fakes;

/// <summary>
/// Request metadata that knows the type only, so <see cref="IRequestMetadata.IsCommand"/> is the
/// interface's default.
/// </summary>
public sealed class TypeOnlyRequestMetadata(Type requestType) : IRequestMetadata
{
    public Type RequestType { get; } = requestType;

    public TAttribute? FindAttribute<TAttribute>() where TAttribute : Attribute => null;

    public IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute => [];

    public bool HasAttribute<TAttribute>() where TAttribute : Attribute => false;
}
