using Bsft.Correlation;
using Bsft.Data.Contracts.Filtering;
using Bsft.Data.Contracts.Pagination;
using Bsft.Hosting.Filtering;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using System.Net.Http.Json;
using System.Net;
using Xunit;
using Bsft.Hosting.DependencyInjection;

namespace Bsft.Hosting.Tests.Filtering;

/// <summary>
/// A minimal controller that binds a <c>[FromQuery] PagedRequest</c> exactly as the CRUD list
/// convention does, and echoes back what bound, so a test can assert the filter transport wired the
/// <c>filter</c> query parameter into <c>PagedRequest.Filter</c>.
/// </summary>
[ApiController]
[Route("api/filter-echo")]
public sealed class FilterEchoController : ControllerBase
{
    /// <summary>Echoes the bound paged request.</summary>
    /// <param name="request">The paged request bound from the query string.</param>
    /// <returns>A projection of what bound.</returns>
    [HttpGet]
    public IActionResult Get([FromQuery] PagedRequest request) => Ok(new
    {
        filterType = request.Filter?.GetType().Name,
        page = request.Page,
        pageSize = request.PageSize,
        sortCount = request.Sort?.Count
    });
}
