using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public record GetGreeting(string Name) : IQuery<string>;
