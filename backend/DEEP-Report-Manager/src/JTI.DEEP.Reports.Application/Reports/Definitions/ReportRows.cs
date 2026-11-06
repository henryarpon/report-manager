namespace JTI.DEEP.Reports.Application.Reports.Definitions;

public static class ReportRows
{
    /// <summary>Converts raw values to display values (enum labels) in place. Same formatting for table and CSV.</summary>
    public static object?[] Format(ResolvedReport report, object?[] row)
    {
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = report.Columns[i].Format(row[i]);
        }

        return row;
    }

    public static async IAsyncEnumerable<object?[]> Format(ResolvedReport report, IAsyncEnumerable<object?[]> rows)
    {
        await foreach (var row in rows)
        {
            yield return Format(report, row);
        }
    }
}
