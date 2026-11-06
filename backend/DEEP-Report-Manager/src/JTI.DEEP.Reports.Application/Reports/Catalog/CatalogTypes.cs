namespace JTI.DEEP.Reports.Application.Reports.Catalog;

/// <summary>What one report row represents.</summary>
public enum ReportRoot
{
    Transaction,
    Customer,
    Program,
}

public enum FieldType
{
    String,
    Date,
    Enum,
    Lookup,
    Number,
}

public enum SortDirection
{
    Asc,
    Desc,
}

public static class ReportEntities
{
    public const string Customer = "Customer";
    public const string Transaction = "Transaction";
    public const string Program = "Program";
}

public static class FilterOperators
{
    public const string Eq = "eq";
    public const string Contains = "contains";
    public const string StartsWith = "startsWith";
    public const string Between = "between";
    public const string Before = "before";
    public const string After = "after";
    public const string In = "in";

    public static readonly IReadOnlyList<string> String = [Eq, Contains, StartsWith];
    public static readonly IReadOnlyList<string> Date = [Between, Before, After];
    public static readonly IReadOnlyList<string> Set = [In];
}

public static class LookupSources
{
    public const string Customers = "customers";
    public const string Programs = "programs";
    public const string Outlets = "outlets";
    public const string Provinces = "provinces";
    public const string FocusZones = "focus-zones";

    public static readonly IReadOnlyList<string> All = [Customers, Programs, Outlets, Provinces, FocusZones];
}

public sealed record EnumOption(int Value, string Label);
