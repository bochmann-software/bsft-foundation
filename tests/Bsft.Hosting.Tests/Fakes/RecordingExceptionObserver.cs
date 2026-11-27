using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>Remembers what it was told.</summary>
internal sealed class RecordingExceptionObserver : IBsftExceptionObserver
{
    public List<(Exception Exception, BsftExceptionMappingResult Result, HttpContext Context)> Told { get; } = [];

    public Task ExceptionMappedAsync(
        Exception exception, BsftExceptionMappingResult result, HttpContext context, CancellationToken cancellationToken)
    {
        Told.Add((exception, result, context));
        return Task.CompletedTask;
    }
}
