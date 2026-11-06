using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Application.Reports.Queries.ExportReport;
using JTI.DEEP.Reports.Application.Reports.Queries.GetReportCatalog;
using JTI.DEEP.Reports.Application.Reports.Queries.Lookup;
using JTI.DEEP.Reports.Application.Reports.Queries.RunReport;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace JTI.DEEP.Reports.API.Controllers;

/// <summary>Thin endpoints: each sends one MediatR query. No business logic here.</summary>
[ApiController]
[Route("api/reports")]
public sealed class ReportsController(ISender mediator) : ControllerBase
{
    [HttpGet("catalog")]
    public Task<CatalogDto> GetCatalog(CancellationToken cancellationToken) =>
        mediator.Send(new GetReportCatalogQuery(), cancellationToken);

    [HttpGet("lookups/{entity}")]
    public Task<IReadOnlyList<LookupItem>> Lookup(
        string entity,
        [FromQuery] string? search,
        [FromQuery] int? take,
        [FromQuery] long[]? programIds,
        CancellationToken cancellationToken) =>
        mediator.Send(new LookupQuery(entity, search, take, programIds), cancellationToken);

    [HttpPost("run")]
    public Task<ReportPage> Run([FromBody] RunReportQuery query, CancellationToken cancellationToken) =>
        mediator.Send(query, cancellationToken);

    [HttpPost("export")]
    public async Task Export([FromBody] ExportReportQuery query, CancellationToken cancellationToken)
    {
        var export = await mediator.Send(query, cancellationToken);

        Response.ContentType = export.ContentType;
        Response.Headers[HeaderNames.ContentDisposition] =
            new ContentDispositionHeaderValue("attachment") { FileNameStar = export.FileName }.ToString();
        Response.Headers["X-Report-Row-Count"] = export.RowCount.ToString();
        Response.Headers[HeaderNames.AccessControlExposeHeaders] = "Content-Disposition, X-Report-Row-Count";

        await export.WriteToAsync(Response.Body, cancellationToken);
    }
}
