using System.Runtime.CompilerServices;
using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using JTI.DEEP.Reports.Application.Reports.Definitions;
using JTI.DEEP.Reports.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JTI.DEEP.Reports.Infrastructure.Reports;

public sealed class EfReportReader(ReportDbContext db) : IReportReader
{
    // Customer and Program have no global soft-delete filter (see ReportDbContext), so their roots exclude
    // deleted rows with Live(). Transactions keep the global filter and stay visible when their parent is deleted.
    public Task<int> CountAsync(ResolvedReport report, CancellationToken cancellationToken) => report.Root.Root switch
    {
        ReportRoot.Transaction => ReportQueryBuilder.Filter(db.Transactions, report).CountAsync(cancellationToken),
        ReportRoot.Customer => ReportQueryBuilder.Filter(db.Customers.Live(), report).CountAsync(cancellationToken),
        ReportRoot.Program => ReportQueryBuilder.Filter(db.Programs.Live(), report).CountAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(report)),
    };

    public async Task<IReadOnlyList<object?[]>> GetPageAsync(ResolvedReport report, int skip, int take,
        CancellationToken cancellationToken) =>
        await Rows(report, skip, take).ToListAsync(cancellationToken);

    public async IAsyncEnumerable<object?[]> StreamAsync(ResolvedReport report,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var row in Rows(report, null, null).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return row;
        }
    }

    private IQueryable<object?[]> Rows(ResolvedReport report, int? skip, int? take) => report.Root.Root switch
    {
        ReportRoot.Transaction => ReportQueryBuilder.Rows(db.Transactions, report, skip, take),
        ReportRoot.Customer => ReportQueryBuilder.Rows(db.Customers.Live(), report, skip, take),
        ReportRoot.Program => ReportQueryBuilder.Rows(db.Programs.Live(), report, skip, take),
        _ => throw new ArgumentOutOfRangeException(nameof(report)),
    };
}
