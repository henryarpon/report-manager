using JTI.DEEP.Reports.Application.Common.Exceptions;
using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using JTI.DEEP.Reports.Application.Reports.Definitions;
using MediatR;
using Microsoft.Extensions.Options;

namespace JTI.DEEP.Reports.Application.Reports.Queries.ExportReport;

public sealed record ExportReportQuery(
    ReportRoot Root,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<ReportFilter>? Filters,
    ReportSort? Sort) : IRequest<ReportExport>, IReportDefinition;

/// <summary>
/// A ready-to-stream export. Nothing is read until <see cref="WriteToAsync"/> runs, so rows go from
/// the database to the response without being buffered.
/// </summary>
public sealed record ReportExport(
    string FileName,
    string ContentType,
    int RowCount,
    Func<Stream, CancellationToken, Task> WriteToAsync);

public sealed class ExportReportQueryValidator(ReportResolver resolver) : ReportDefinitionValidator<ExportReportQuery>(resolver);

public sealed class ExportReportQueryHandler(
    ReportResolver resolver,
    IReportReader reader,
    IReportFileWriter writer,
    IOptions<ReportOptions> options,
    TimeProvider clock) : IRequestHandler<ExportReportQuery, ReportExport>
{
    public async Task<ReportExport> Handle(ExportReportQuery request, CancellationToken cancellationToken)
    {
        var report = resolver.Resolve(request);
        var settings = options.Value;

        var total = await reader.CountAsync(report, cancellationToken);
        if (total > settings.MaxExportRows)
        {
            throw OperationalException.WithMessage(
                $"This report has {total:N0} rows, more than the export limit of {settings.MaxExportRows:N0}. Add filters to narrow it down.",
                "filters");
        }

        var headers = report.Columns.Select(c => c.Label).ToList();
        var baseName = $"{report.Root.Root.ToString().ToLowerInvariant()}-report-{clock.GetLocalNow():yyyyMMdd-HHmmss}";

        IAsyncEnumerable<object?[]> Rows(CancellationToken ct) => ReportRows.Format(report, reader.StreamAsync(report, ct));

        if (total <= settings.ExportChunkRows)
        {
            return new ReportExport($"{baseName}.csv", "text/csv", total,
                (stream, ct) => writer.WriteCsvAsync(stream, headers, Rows(ct), ct));
        }

        return new ReportExport($"{baseName}.zip", "application/zip", total,
            (stream, ct) => writer.WriteZipAsync(stream, headers, Rows(ct), settings.ExportChunkRows, $"{baseName}_part", ct));
    }
}
