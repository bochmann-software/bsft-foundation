using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Bsft.Resilience;

/// <summary>
/// Retry for streams. Wraps a factory that produces an <see cref="IAsyncEnumerable{T}"/> so that a
/// failure part-way through the enumeration is classified, backed off and retried by re-invoking the
/// factory, instead of tearing the enumeration down at the first transient fault.
/// </summary>
/// <remarks>
/// The rest of this package retries request/response calls, where a retry is invisible to the caller.
/// A stream is different: by the time it breaks, items are already downstream, and re-invoking the
/// factory produces them again. That hazard is not hidden here — it is
/// <see cref="PartialStreamPolicy"/>, the caller picks, and the default picks the side that never
/// duplicates. Read the policy's documentation before switching it.
/// <para>
/// Extension methods on the factory rather than a registered service: there is nothing to resolve. The
/// factory and the classifier are the caller's, the options are per call, and a container-wide instance
/// would be one budget for streams that do not share one. The package therefore gains no second
/// <c>ServiceCollectionExtensions</c>. The factory is the value being transformed, so a held factory
/// reads as <c>factory.WithRetry(…)</c> while an inline lambda still passes as an ordinary argument.
/// </para>
/// </remarks>
public static class ResilientStreamExtensions
{
    /// <summary>
    /// Wraps a stream factory in classifier-driven retry. The factory is invoked once per attempt and
    /// its stream is enumerated until it completes or throws.
    /// </summary>
    /// <typeparam name="T">The item type of the stream.</typeparam>
    /// <param name="factory">Produces the stream for an attempt. Invoked again, from scratch, per retry.</param>
    /// <param name="configure">Adjusts the defaults of <see cref="BsftResilienceStreamOptions"/>.</param>
    /// <param name="cancellationToken">Stops the enumeration and any pending backoff.</param>
    /// <returns>A stream that retries according to the options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    public static IAsyncEnumerable<T> WithRetry<T>(
        this Func<CancellationToken, IAsyncEnumerable<T>> factory,
        Action<BsftResilienceStreamOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return WithRetry((_, token) => factory(token), configure, cancellationToken);
    }

    /// <summary>
    /// Wraps a stream factory that is told what the previous attempts already delivered, so it can resume
    /// rather than restart. See <see cref="StreamRetryContext"/>.
    /// </summary>
    /// <typeparam name="T">The item type of the stream.</typeparam>
    /// <param name="factory">Produces the stream for an attempt, given the attempt's context.</param>
    /// <param name="configure">Adjusts the defaults of <see cref="BsftResilienceStreamOptions"/>.</param>
    /// <param name="cancellationToken">Stops the enumeration and any pending backoff.</param>
    /// <returns>A stream that retries according to the options.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a configured value cannot be used, naming the member and the value.</exception>
    /// <remarks>
    /// The options are validated here, before anything is enumerated: an unusable budget fails on this
    /// call rather than on the first fault, which may be far away in both stack and time.
    /// </remarks>
    public static IAsyncEnumerable<T> WithRetry<T>(
        this Func<StreamRetryContext, CancellationToken, IAsyncEnumerable<T>> factory,
        Action<BsftResilienceStreamOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var options = new BsftResilienceStreamOptions();
        configure?.Invoke(options);
        options.Validate();

        return EnumerateAsync(factory, options, cancellationToken);
    }

    /// <summary>Runs the attempts: enumerate, classify what broke, back off, start the next one.</summary>
    /// <typeparam name="T">The item type of the stream.</typeparam>
    /// <param name="factory">Produces the stream for an attempt.</param>
    /// <param name="options">The validated retry options.</param>
    /// <param name="cancellationToken">Stops the enumeration and any pending backoff.</param>
    /// <returns>The retrying stream.</returns>
    private static async IAsyncEnumerable<T> EnumerateAsync<T>(
        Func<StreamRetryContext, CancellationToken, IAsyncEnumerable<T>> factory,
        BsftResilienceStreamOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var state = new AttemptState();

        for (var retriesUsed = 0; ; retriesUsed++)
        {
            state.Reset();
            var source = factory(new StreamRetryContext(retriesUsed, state.ItemsYielded), cancellationToken);

            await foreach (var item in AttemptAsync(source, state, cancellationToken).ConfigureAwait(false))
                yield return item;

            var failure = state.Failure;

            if (failure is null)
                yield break;

            if (!CanRetry(options, failure, retriesUsed, state.ItemsYielded))
                ExceptionDispatchInfo.Throw(failure);

            // Checked here rather than only inside the wait: a retry budget with no backoff would
            // otherwise spin through its attempts without ever observing the token.
            cancellationToken.ThrowIfCancellationRequested();

            var delay = options.DelayBeforeRetry(retriesUsed + 1);

            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, options.TimeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Enumerates one attempt, recording what it delivered and what broke it instead of throwing.
    /// </summary>
    /// <typeparam name="T">The item type of the stream.</typeparam>
    /// <param name="source">The stream this attempt produced.</param>
    /// <param name="state">Carries the running item count out, and the failure if there was one.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    /// <returns>The items of this attempt, up to the point it completed or failed.</returns>
    /// <remarks>
    /// An iterator of its own because C# forbids <c>yield return</c> inside a <c>try</c> that has a
    /// <c>catch</c>. Here the <c>catch</c> guards only the pull, the item is yielded after it, and the
    /// failure leaves through <paramref name="state"/> rather than up the stack — which is also what lets
    /// the caller decide whether it is worth another attempt. <see cref="OperationCanceledException"/> is
    /// never captured: it means the enumeration was asked to stop, so it propagates and ends the stream.
    /// The enumerator is disposed on every path, including the consumer abandoning the stream mid-flight.
    /// </remarks>
    private static async IAsyncEnumerable<T> AttemptAsync<T>(
        IAsyncEnumerable<T> source,
        AttemptState state,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = source.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                T item;
                bool moved;

                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    item = moved ? enumerator.Current : default!;
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    state.Capture(failure);
                    break;
                }

                if (!moved)
                    break;

                state.ItemsYielded++;
                yield return item;
            }
        }
        finally
        {
            await ReleaseAsync(enumerator, state).ConfigureAwait(false);
        }
    }

    /// <summary>Disposes the attempt's enumerator, routing a teardown fault through the same capture.</summary>
    /// <typeparam name="T">The item type of the stream.</typeparam>
    /// <param name="enumerator">The enumerator to dispose.</param>
    /// <param name="state">Carries the failure out, if disposal is the first thing to fail.</param>
    /// <returns>A task that completes once the enumerator is disposed.</returns>
    /// <remarks>
    /// Letting the teardown throw on its own would replace an already-recorded failure with the cleanup
    /// fault, and the replacement would leave through the <c>finally</c> without ever reaching the
    /// classifier — so a transient break whose source also faults on disposal would never be retried.
    /// Capturing it keeps one rule for the whole attempt: whatever the stream throws, while producing
    /// items or while being torn down, is offered to the classifier, and the first failure wins.
    /// <para>
    /// A cancellation raised by the disposal itself is captured too, but only once the attempt already
    /// has a failure to report. Then it is a teardown fault like any other, and letting it out would
    /// throw away the exception that actually broke the stream. With nothing recorded yet it still
    /// propagates: the enumeration was asked to stop, and that ends the stream rather than earning a
    /// retry.
    /// </para>
    /// </remarks>
    private static async ValueTask ReleaseAsync<T>(IAsyncEnumerator<T> enumerator, AttemptState state)
    {
        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || state.Failure is not null)
        {
            state.Capture(failure);
        }
    }

    /// <summary>Decides whether the failure that ended an attempt earns another one.</summary>
    /// <param name="options">The validated retry options.</param>
    /// <param name="failure">What ended the attempt.</param>
    /// <param name="retriesUsed">How many retries have already been spent.</param>
    /// <param name="itemsYielded">How many items the consumer has already been handed.</param>
    /// <returns><see langword="true"/> when another attempt is to be made; otherwise <see langword="false"/>.</returns>
    private static bool CanRetry(
        BsftResilienceStreamOptions options, Exception failure, int retriesUsed, long itemsYielded)
    {
        if (retriesUsed >= options.MaxRetryAttempts)
            return false;

        if (itemsYielded > 0 && options.PartialStreamPolicy == PartialStreamPolicy.RethrowMidStream)
            return false;

        return options.ShouldRetry(failure);
    }

    /// <summary>What one attempt reports back to the loop that started it.</summary>
    private sealed class AttemptState
    {
        /// <summary>Gets what ended the attempt, or <see langword="null"/> when it completed.</summary>
        public Exception? Failure { get; private set; }

        /// <summary>Gets or sets how many items the consumer has been handed across all attempts.</summary>
        public long ItemsYielded { get; set; }

        /// <summary>Records the failure that ended the attempt, keeping the first one.</summary>
        /// <param name="failure">What the stream threw.</param>
        /// <remarks>
        /// The first one, because it is the one that describes why the stream stopped producing; a fault
        /// raised while tearing down the already-broken enumerator would otherwise hide it. The one that
        /// gives way is not dropped: a disposal that genuinely failed — a connection that would not close,
        /// a cursor that would not release — stays diagnosable under
        /// <see cref="ResilienceDefaults.SuppressedFailureDataKey"/> in <see cref="Exception.Data"/> of the
        /// failure the caller is given.
        /// </remarks>
        public void Capture(Exception failure)
        {
            if (Failure is null)
            {
                Failure = failure;
                return;
            }

            Failure.Data[ResilienceDefaults.SuppressedFailureDataKey] = failure;
        }

        /// <summary>Clears the previous attempt's failure before the next one starts.</summary>
        public void Reset() => Failure = null;
    }
}
