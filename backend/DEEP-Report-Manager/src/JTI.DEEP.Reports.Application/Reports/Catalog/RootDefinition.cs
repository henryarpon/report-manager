namespace JTI.DEEP.Reports.Application.Reports.Catalog;

public sealed class RootDefinition(
    ReportRoot root,
    string label,
    IReadOnlyList<ColumnDefinition> columns,
    string defaultSortColumn,
    SortDirection defaultSortDirection,
    IReadOnlyDictionary<string, string> filterScopes)
{
    private readonly Dictionary<string, ColumnDefinition> _byKey =
        columns.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

    public ReportRoot Root { get; } = root;
    public string Label { get; } = label;
    public IReadOnlyList<ColumnDefinition> Columns { get; } = columns;
    public IEnumerable<ColumnDefinition> DefaultColumns => Columns.Where(c => c.IsDefault);
    public ColumnDefinition DefaultSortColumn => _byKey[defaultSortColumn];
    public SortDirection DefaultSortDirection { get; } = defaultSortDirection;

    /// <summary>Per filter entity, how filters on it relate to this root (shown next to the filter in the UI).</summary>
    public IReadOnlyDictionary<string, string> FilterScopes { get; } = filterScopes;

    public ColumnDefinition? FindColumn(string key) => _byKey.GetValueOrDefault(key);
}
