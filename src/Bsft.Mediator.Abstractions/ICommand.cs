using Bsft.Core;

namespace Bsft.Mediator;

/// <summary>
/// Marker interface for a command that produces a result of type <typeparamref name="TResult"/>.
/// </summary>
/// <typeparam name="TResult">The type of the result produced by the command.</typeparam>
public interface ICommand<TResult>;

/// <summary>
/// Marker interface for a command that does not produce a result.
/// Syntactic sugar for <see cref="ICommand{TResult}"/> with <see cref="Unit"/>.
/// </summary>
public interface ICommand : ICommand<Unit>;
