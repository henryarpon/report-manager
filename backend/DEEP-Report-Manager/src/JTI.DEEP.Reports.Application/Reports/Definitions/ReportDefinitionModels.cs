using System.Text.Json;
using JTI.DEEP.Reports.Application.Reports.Catalog;

namespace JTI.DEEP.Reports.Application.Reports.Definitions;

/// <summary>The report definition shared by the run (paged) and export requests.</summary>
public interface IReportDefinition
{
    ReportRoot Root { get; }
    IReadOnlyList<string>? Columns { get; }
    IReadOnlyList<ReportFilter>? Filters { get; }
    ReportSort? Sort { get; }
}

/// <summary>One filter as sent by the client. <see cref="Value"/> shape depends on the operator.</summary>
public sealed record ReportFilter(string? Entity, string? Field, string? Op, JsonElement Value);

public sealed record ReportSort(string? Column, SortDirection? Dir);

/// <summary>Date bounds already converted to half-open form: From &lt;= value &lt; ToExclusive.</summary>
public sealed record DateRange(DateTime? From, DateTime? ToExclusive);

/// <summary>A filter matched to its catalog entry with a typed value (string, string[], long[], enum[], DateRange).</summary>
public sealed record ParsedFilter(FilterDefinition Definition, string Op, object Value);

/// <summary>A validated definition: every column, filter and sort refers to a whitelisted catalog entry.</summary>
public sealed record ResolvedReport(
    RootDefinition Root,
    IReadOnlyList<ColumnDefinition> Columns,
    IReadOnlyList<ParsedFilter> Filters,
    ColumnDefinition SortColumn,
    SortDirection SortDirection);
