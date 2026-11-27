using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>An observer that always fails.</summary>
internal sealed class ThrowingExceptionObserver : IBsftExceptionObserver
{
    public Task ExceptionMappedAsync(
        Exception exception, BsftExceptionMappingResult result, HttpContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("audit store down");
}
