using Bsft.Mediator.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bsft.Mediator.Tests;

/// <summary>
/// The shape a consumer used to need a <c>ConcurrentDictionary&lt;Type, ...&gt;</c> of its own for.
/// </summary>
internal sealed class UsageRecordingBehavior<TRequest, TResponse>(
    IRequestMetadataProvider metadataProvider,
    UsageRecorder recorder)
    : IPipelineBehavior<TRequest, TResponse>
{
    public Task<TResponse> HandleAsync(TRequest request, Func<Task<TResponse>> next, CancellationToken ct = default)
    {
        var metadata = metadataProvider.GetMetadata(request!);
        recorder.RequestTypes.Add(metadata.RequestType);

        if (metadata.FindAttribute<UsageAttribute>() is { } usage)
            recorder.Features.Add(usage.Feature);

        return next();
    }
}
