using Bsft.Mediator;

namespace Bsft.Data.Postgres.Tests.Support;

/// <summary>Command that always fails with a serialization error (40001).</summary>
[Bsft.Mediator.Retryable]
public sealed record AlwaysFailSerializationCommand : ICommand<int>;
