namespace Bsft.Mediator;

/// <summary>
/// One attempt of an <see cref="IRescopingPipelineBehavior{TRequest, TResponse}"/> at the rest of the
/// pipeline, handed to <see cref="IPipelineContinuation{TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The type of the response.</typeparam>
/// <param name="scope">The provider of the scope the rest of the pipeline runs in.</param>
/// <param name="rest">Runs the later behaviors and the handler, resolved from <paramref name="scope"/>.</param>
/// <returns>The response of the attempt.</returns>
public delegate Task<TResponse> PipelineAttempt<TResponse>(IServiceProvider scope, Func<Task<TResponse>> rest);
