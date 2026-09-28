using Microsoft.Extensions.DependencyInjection;

namespace Bsft.Testing.Mediation;

/// <summary>
/// Assembles a mediator that contains only what the test under construction needs.
/// </summary>
/// <remarks>
/// Nothing is scanned or inferred: a handler runs because the test asked for it, and a behavior sits
/// in the pipeline because the test put it there. That is the point of the builder — an assembly
/// scan drags in every handler and every behavior of the project, and then a test failure no longer
/// tells you which of them broke.
/// </remarks>
public interface IMediatorHarnessBuilder
{
    /// <summary>Registers a handler against every handler interface it implements.</summary>
    /// <typeparam name="THandler">The handler implementation.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    IMediatorHarnessBuilder WithHandler<THandler>()
        where THandler : class;

    /// <summary>Registers a handler against every handler interface it implements.</summary>
    /// <param name="handlerType">The handler implementation.</param>
    /// <returns>The same builder, for chaining.</returns>
    IMediatorHarnessBuilder WithHandler(Type handlerType);

    /// <summary>
    /// Adds a pipeline behavior. Behaviors run in the order they are added, the first one outermost.
    /// </summary>
    /// <param name="behaviorType">
    /// The behavior, either an open generic such as <c>typeof(LoggingBehavior&lt;,&gt;)</c> or a
    /// closed type.
    /// </param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// The mediator resolves behaviors by the *static* request type it was called with — that is
    /// <c>ICommand&lt;TResult&gt;</c> or <c>IQuery&lt;TResult&gt;</c>, never the concrete command
    /// class. A behavior closed over a concrete command therefore never runs; close it over
    /// <c>ICommand&lt;TResult&gt;</c>, or use an open generic and branch on the request inside.
    /// </remarks>
    IMediatorHarnessBuilder WithBehavior(Type behaviorType);

    /// <summary>Registers a collaborator a handler or behavior depends on, such as a mock.</summary>
    /// <typeparam name="TService">The service type to resolve the instance as.</typeparam>
    /// <param name="instance">The instance to register.</param>
    /// <returns>The same builder, for chaining.</returns>
    IMediatorHarnessBuilder WithService<TService>(TService instance)
        where TService : class;

    /// <summary>Registers services that need more than a single instance registration.</summary>
    /// <param name="configure">Applied to the container while it is being assembled.</param>
    /// <returns>The same builder, for chaining.</returns>
    IMediatorHarnessBuilder WithServices(Action<IServiceCollection> configure);

    /// <summary>
    /// Registers the tenant and correlation test doubles, for handlers that depend on them.
    /// </summary>
    /// <returns>The same builder, for chaining.</returns>
    IMediatorHarnessBuilder WithTestContexts();

    /// <summary>Builds the container and returns the harness.</summary>
    /// <returns>The assembled harness, which the caller must dispose.</returns>
    IMediatorTestHarness Build();
}
