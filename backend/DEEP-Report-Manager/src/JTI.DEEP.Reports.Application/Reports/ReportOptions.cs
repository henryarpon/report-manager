namespace JTI.DEEP.Reports.Application.Reports;

public sealed class ReportOptions
{
    public const string SectionName = "Reports";

    public int DefaultPageSize { get; set; } = 25;
    public int MaxPageSize { get; set; } = 100;

    /// <summary>Rows per CSV file. Exports larger than this are split into a ZIP of parts.</summary>
    public int ExportChunkRows { get; set; } = 50_000;

    /// <summary>Hard ceiling for one export. Larger results must be narrowed with filters.</summary>
    public int MaxExportRows { get; set; } = 500_000;

    public int MaxLookupResults { get; set; } = 20;
    public int CustomerSearchMinLength { get; set; } = 3;
}
