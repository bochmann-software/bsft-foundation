using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Bsft.Http.Egress.DependencyInjection;

/// <summary>
/// Extension methods that put the egress guard on an <see cref="IHttpClientBuilder"/>.
/// </summary>
/// <remarks>
/// A class of its own rather than part of <see cref="HttpEgressServiceCollectionExtensions"/>: the
/// "exactly one entry point per package" rule is about the <see cref="IServiceCollection"/> surface,
/// and this is a different extension family, on a builder a host already holds.
/// </remarks>
public static class HttpEgressHttpClientBuilderExtensions
{
    /// <summary>
    /// Guards a named or typed client: the request-time handler on the chain, and the connect-time
    /// guard as the primary handler's connect callback.
    /// </summary>
    /// <param name="builder">The client builder to guard.</param>
    /// <param name="configureHandler">
    /// Adjusts the primary handler this call creates — redirect and cookie behaviour, timeouts. It runs
    /// before the connect callback is attached, so a caller cannot replace the guard by accident.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// It replaces the client's primary handler, which is the only place a connect callback can live.
    /// Anything the client needs on that handler is set through <paramref name="configureHandler"/>
    /// rather than by a second <c>ConfigurePrimaryHttpMessageHandler</c> call, because the last such
    /// call wins and the loser would be this one.
    /// </para>
    /// <para>
    /// The handler lifetime is left at its default. A handler that is recycled periodically is what
    /// keeps DNS changes from being pinned by a pooled connection, which matters more here than
    /// anywhere else in an application: the whole point of the connect callback is that the address is
    /// resolved and judged afresh.
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddBsftHttpEgressGuard(
        this IHttpClientBuilder builder, Action<SocketsHttpHandler>? configureHandler = null)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        return Add(builder, _ => true, configureHandler);
    }

    /// <summary>
    /// Guards a client when the host's own configuration says to, deciding it when the client's
    /// handler chain is built rather than when it is registered.
    /// </summary>
    /// <param name="builder">The client builder to guard.</param>
    /// <param name="isEnabled">
    /// Reads the switch off the container. It is called once per handler chain the client factory
    /// builds, so the answer comes from the options the application actually runs on.
    /// </param>
    /// <param name="configureHandler">
    /// Adjusts the primary handler this call creates, and runs only when the guard is on: what it sets
    /// belongs to the guard rather than to the client.
    /// </param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> or <paramref name="isEnabled"/> is null.
    /// </exception>
    /// <remarks>
    /// The overload exists so a package with a switch of its own does not have to read that switch
    /// before the container exists — which would mean running the host's configuration delegate a
    /// second time, against a throwaway options instance, to find out. With the guard off the client
    /// is left exactly as it would have been: a pass-through on the chain, and the primary handler the
    /// client factory would have given it.
    /// </remarks>
    public static IHttpClientBuilder AddBsftHttpEgressGuard(
        this IHttpClientBuilder builder,
        Func<IServiceProvider, bool> isEnabled,
        Action<SocketsHttpHandler>? configureHandler = null)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));

        if (isEnabled is null)
            throw new ArgumentNullException(nameof(isEnabled));

        return Add(builder, isEnabled, configureHandler);
    }

    /// <summary>Puts both halves on the client, whichever overload was called.</summary>
    /// <param name="builder">The client builder to guard.</param>
    /// <param name="isEnabled">Reads the switch off the container.</param>
    /// <param name="configureHandler">Adjusts the primary handler, when the guard is on.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    /// <remarks>
    /// Both halves are attached from one action over the handler builder rather than through
    /// <c>AddHttpMessageHandler</c> and <c>ConfigurePrimaryHttpMessageHandler</c> separately, so the
    /// switch is asked exactly once per chain. Two questions could be answered differently — by a
    /// predicate that reads something mutable — and the client would come out half guarded: the
    /// request-time handler in place while the connect-time half, the one that closes the rebinding
    /// race, is quietly missing. One question cannot disagree with itself. It also means a client with
    /// the guard off is left completely untouched, rather than being handed a pass-through link and a
    /// primary handler this package chose for it.
    /// </remarks>
    private static IHttpClientBuilder Add(
        IHttpClientBuilder builder,
        Func<IServiceProvider, bool> isEnabled,
        Action<SocketsHttpHandler>? configureHandler)
    {
        builder.Services.Configure<HttpClientFactoryOptions>(
            builder.Name,
            options => options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
            {
                if (!isEnabled(handlerBuilder.Services))
                    return;

                handlerBuilder.AdditionalHandlers.Add(
                    handlerBuilder.Services.GetRequiredService<EgressGuardHandler>());

                handlerBuilder.PrimaryHandler = Primary(handlerBuilder.Services, configureHandler);
            }));

        return builder;
    }

    /// <summary>Builds the primary handler that connects through the guard.</summary>
    /// <param name="provider">The provider the chain is being built from.</param>
    /// <param name="configureHandler">Adjusts the handler before the callback is attached.</param>
    /// <returns>The primary handler.</returns>
    private static SocketsHttpHandler Primary(
        IServiceProvider provider, Action<SocketsHttpHandler>? configureHandler)
    {
        var guard = provider.GetRequiredService<IOutboundConnectGuard>();

        var handler = new SocketsHttpHandler();

        configureHandler?.Invoke(handler);

        handler.ConnectCallback = async (context, ct) =>
            await guard
                .ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, ct)
                .ConfigureAwait(false);

        return handler;
    }
}
