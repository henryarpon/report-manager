using JTI.DEEP.Reports.Application.Reports.Definitions;

namespace JTI.DEEP.Reports.Application.Reports.Abstractions;

/// <summary>
/// The shared query builder. Run (paged) and export (streamed) both go through it, so the table
/// and the CSV always contain the same rows in the same order.
/// Rows are raw column values in the order of <see cref="ResolvedReport.Columns"/>.
/// </summary>
public interface IReportReader
{
    Task<int> CountAsync(ResolvedReport report, CancellationToken cancellationToken);

    Task<IReadOnlyList<object?[]>> GetPageAsync(ResolvedReport report, int skip, int take, CancellationToken cancellationToken);

    IAsyncEnumerable<object?[]> StreamAsync(ResolvedReport report, CancellationToken cancellationToken);
}

public sealed record LookupItem(object Value, string Label, long? ParentId = null);

public interface ILookupReader
{
    Task<IReadOnlyList<LookupItem>> CustomersAsync(string search, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> ProgramsAsync(string search, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> OutletsAsync(IReadOnlyCollection<long> programIds, string? search, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> ProvincesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LookupItem>> FocusZonesAsync(CancellationToken cancellationToken);
}

/// <summary>Writes report rows as CSV (single file) or as a ZIP of CSV parts, streaming throughout.</summary>
public interface IReportFileWriter
{
    Task WriteCsvAsync(Stream output, IReadOnlyList<string> headers, IAsyncEnumerable<object?[]> rows,
        CancellationToken cancellationToken);

    Task WriteZipAsync(Stream output, IReadOnlyList<string> headers, IAsyncEnumerable<object?[]> rows,
        int rowsPerFile, string partFilePrefix, CancellationToken cancellationToken);
}
