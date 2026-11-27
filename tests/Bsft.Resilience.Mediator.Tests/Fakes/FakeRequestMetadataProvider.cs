using Bsft.Mediator;

namespace Bsft.Resilience.Mediator.Tests.Fakes;

/// <summary>Says which requests are commands, the one question the retry behavior asks the metadata.</summary>
public sealed class FakeRequestMetadataProvider : IRequestMetadataProvider
{
    public IRequestMetadata GetMetadata(object request) => GetMetadata(request.GetType());

    public IRequestMetadata GetMetadata(Type requestType) => new Metadata(requestType);

    private sealed class Metadata(Type requestType) : IRequestMetadata
    {
        public Type RequestType => requestType;

        public bool IsCommand => Array.Exists(requestType.GetInterfaces(), contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(ICommand<>));

        public TAttribute? FindAttribute<TAttribute>() where TAttribute : Attribute => null;

        public IReadOnlyList<TAttribute> ListAttributes<TAttribute>() where TAttribute : Attribute => [];

        public bool HasAttribute<TAttribute>() where TAttribute : Attribute => false;
    }
}
