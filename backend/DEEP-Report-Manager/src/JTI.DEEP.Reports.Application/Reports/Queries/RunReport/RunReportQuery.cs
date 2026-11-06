using FluentValidation;
using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using JTI.DEEP.Reports.Application.Reports.Definitions;
using MediatR;
using Microsoft.Extensions.Options;

namespace JTI.DEEP.Reports.Application.Reports.Queries.RunReport;

public sealed record RunReportQuery(
    ReportRoot Root,
    IReadOnlyList<string>? Columns,
    IReadOnlyList<ReportFilter>? Filters,
    ReportSort? Sort,
    int Page = 1,
    int? PageSize = null) : IRequest<ReportPage>, IReportDefinition;

public sealed record ColumnHeader(string Key, string Label, FieldType Type);

public sealed record ReportPage(
    IReadOnlyList<ColumnHeader> Columns,
    IReadOnlyList<object?[]> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class RunReportQueryValidator : ReportDefinitionValidator<RunReportQuery>
{
    public RunReportQueryValidator(ReportResolver resolver, IOptions<ReportOptions> options) : base(resolver)
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, options.Value.MaxPageSize)
            .When(x => x.PageSize is not null);
    }
}

public sealed class RunReportQueryHandler(
    ReportResolver resolver,
    IReportReader reader,
    IOptions<ReportOptions> options) : IRequestHandler<RunReportQuery, ReportPage>
{
    public async Task<ReportPage> Handle(RunReportQuery request, CancellationToken cancellationToken)
    {
        var report = resolver.Resolve(request);
        var pageSize = request.PageSize ?? options.Value.DefaultPageSize;

        var total = await reader.CountAsync(report, cancellationToken);
        var rows = await reader.GetPageAsync(report, (request.Page - 1) * pageSize, pageSize, cancellationToken);

        return new ReportPage(
            report.Columns.Select(c => new ColumnHeader(c.Key, c.Label, c.Type)).ToList(),
            rows.Select(row => ReportRows.Format(report, row)).ToList(),
            total,
            request.Page,
            pageSize);
    }
}
