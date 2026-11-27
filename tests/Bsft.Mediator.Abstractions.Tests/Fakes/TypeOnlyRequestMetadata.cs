namespace Bsft.Mediator.Abstractions.Tests.Fakes;

/// <summary>Request metadata written before <see cref="IRequestMetadata.IsCommand"/> existed: it knows the type only.</summary>
public sealed class TypeOnlyRequestMetadata(Type requestType) : IRequestMetadata
{
    public Type RequestType { get; } = requestType;

    public TAttribute? FindAttribute<TAttribute>() where TAttribute : Attribute => null;

    public IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute => [];

    public bool HasAttribute<TAttribute>() where TAttribute : Attribute => false;
}
