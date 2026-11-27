using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>Maps <see cref="UnauthorizedAccessException"/> to a 401 that names a retry header.</summary>
internal sealed class UnauthorizedMapper : IBsftExceptionMapper
{
    public BsftExceptionMappingResult? Map(Exception exception, HttpContext context, string correlationId) =>
        exception is UnauthorizedAccessException
            ? new BsftExceptionMappingResult(
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                "Sign in first.",
                new Dictionary<string, object> { ["errorCode"] = "AUTH_REQUIRED", ["correlationId"] = correlationId },
                new Dictionary<string, string> { ["Retry-After"] = "30" },
                "https://example.test/problems/unauthorized")
            : null;
}
