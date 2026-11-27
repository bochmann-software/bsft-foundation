using Microsoft.AspNetCore.Http;

namespace Bsft.Hosting.Tests.Fakes;

/// <summary>Maps every exception to a 409, so a mapper registered before it takes the exception first.</summary>
internal sealed class CatchAllConflictMapper : IBsftExceptionMapper
{
    public BsftExceptionMappingResult? Map(Exception exception, HttpContext context, string correlationId) =>
        new(StatusCodes.Status409Conflict, "Late Conflict");
}
