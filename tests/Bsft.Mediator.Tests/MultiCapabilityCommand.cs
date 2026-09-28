using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

[Capability("read")]
[Capability("write")]
internal sealed record MultiCapabilityCommand : ICommand<string>;
