using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>Maps a <see cref="NotSupportedException"/> to a 500 whose detail quotes the exception.</summary>
internal sealed class LeakyInternalErrorMapper : IBsftExceptionMapper
{
    public BsftExceptionMappingResult? Map(Exception exception, HttpContext context, string correlationId) =>
        exception is NotSupportedException
            ? new BsftExceptionMappingResult(
                StatusCodes.Status500InternalServerError,
                "Custom Failure",
                exception.Message,
                new Dictionary<string, object> { ["errorCode"] = "CUSTOM" })
            : null;
}
