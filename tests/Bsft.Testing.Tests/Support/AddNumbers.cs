using Bsft.Core;
using Bsft.Mediator;

namespace Bsft.Testing.Tests.Support;

public record AddNumbers(int Left, int Right) : ICommand<int>;
