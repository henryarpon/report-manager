using System.Linq.Expressions;
using JTI.DEEP.Reports.Domain.Entities;
using JTI.DEEP.Reports.Domain.Enums;

namespace JTI.DEEP.Reports.Application.Reports.Catalog;

/// <summary>
/// The single metadata registry for reports. It drives the catalog endpoint (Angular pickers),
/// request validation and the query builder. Adding a filter or column = adding one entry here.
/// </summary>
/// <remarks>
/// A transaction is "live" when it is not soft-deleted itself; a deleted customer, program or outlet does
/// not hide it. Customer, Program and ProgramOutlet have no global IsDeleted filter (see ReportDbContext).
/// </remarks>
public sealed class ReportCatalog
{
    private readonly Dictionary<string, FilterDefinition> _filters;

    public ReportCatalog()
    {
        Roots = new Dictionary<ReportRoot, RootDefinition>
        {
            [ReportRoot.Transaction] = BuildTransactionRoot(),
            [ReportRoot.Customer] = BuildCustomerRoot(),
            [ReportRoot.Program] = BuildProgramRoot(),
        };
        Filters = BuildFilters();
        _filters = Filters.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<ReportRoot, RootDefinition> Roots { get; }
    public IReadOnlyList<FilterDefinition> Filters { get; }

    public FilterDefinition? FindFilter(string? entity, string? field) =>
        entity is null || field is null ? null : _filters.GetValueOrDefault($"{entity}.{field}");

    // ---------- Roots and columns ----------

    private static RootDefinition BuildTransactionRoot()
    {
        var columns = new List<ColumnDefinition>
        {
            Col<Transaction, long>("Transaction.Id", "Transaction ID", FieldType.Number, t => t.Id),
            Col<Transaction, DateTime>("Transaction.StartTransactionDate", "Transaction date", FieldType.Date, t => t.StartTransactionDate, isDefault: true),
            Col<Transaction, DateTime>("Transaction.EndTransactionDate", "Transaction end date", FieldType.Date, t => t.EndTransactionDate),
            Col<Transaction, TransactionPloomType>("Transaction.PloomType", "Transaction Ploom type", FieldType.Enum, t => t.PloomType, isDefault: true),

            Col<Transaction, string?>("Customer.FullName", "Customer name", FieldType.String, t => t.Customer!.FullName, isDefault: true),
            Col<Transaction, string?>("Customer.FirstName", "Customer first name", FieldType.String, t => t.Customer!.FirstName),
            Col<Transaction, string?>("Customer.LastName", "Customer last name", FieldType.String, t => t.Customer!.LastName),
            Col<Transaction, string?>("Customer.EmailAddress", "Customer email", FieldType.String, t => t.Customer!.EmailAddress, isDefault: true, sensitive: true),
            Col<Transaction, string?>("Customer.MobileNumber", "Customer mobile", FieldType.String, t => t.Customer!.MobileNumber, sensitive: true),
            Col<Transaction, CustomerType>("Customer.CustomerType", "Customer type", FieldType.Enum, t => t.Customer!.CustomerType),
            Col<Transaction, PloomType>("Customer.PloomType", "Customer Ploom type", FieldType.Enum, t => t.Customer!.PloomType),
            Col<Transaction, string?>("Customer.Province", "Customer province", FieldType.String, t => t.Customer!.Province!.Name),

            Col<Transaction, string?>("Program.ProgramName", "Program", FieldType.String, t => t.Program!.ProgramName, isDefault: true),
            Col<Transaction, ProgramType>("Program.ProgramType", "Program type", FieldType.Enum, t => t.Program!.ProgramType),
            Col<Transaction, ProgramStatus>("Program.Status", "Program status", FieldType.Enum, t => t.Program!.Status),
            Col<Transaction, string?>("Program.FocusZone", "Program focus zone", FieldType.String, t => t.Program!.FocusZone),

            // ProgramOutlet has no global soft-delete filter (see ReportDbContext), so deleted outlets are blanked here.
            Col<Transaction, string?>("Outlet.OutletName", "Outlet", FieldType.String, t => t.Outlet!.IsDeleted ? null : t.Outlet.OutletName, isDefault: true),
            Col<Transaction, string?>("Outlet.OutletCode", "Outlet code", FieldType.String, t => t.Outlet!.IsDeleted ? null : t.Outlet.OutletCode),
            Col<Transaction, string?>("Outlet.City", "Outlet city", FieldType.String, t => t.Outlet!.IsDeleted ? null : t.Outlet.City),
            Col<Transaction, string?>("Outlet.Province", "Outlet province", FieldType.String, t => t.Outlet!.IsDeleted ? null : t.Outlet.Province),
        };

        return new RootDefinition(ReportRoot.Transaction, "Transactions", columns,
            "Transaction.StartTransactionDate", SortDirection.Desc,
            new Dictionary<string, string>
            {
                [ReportEntities.Transaction] = "Transaction fields",
                [ReportEntities.Customer] = "Customer of the transaction",
                [ReportEntities.Program] = "Program of the transaction",
            });
    }

    private static RootDefinition BuildCustomerRoot()
    {
        var columns = new List<ColumnDefinition>
        {
            Col<Customer, long>("Customer.Id", "Customer ID", FieldType.Number, c => c.Id),
            Col<Customer, string?>("Customer.FullName", "Name", FieldType.String, c => c.FullName, isDefault: true),
            Col<Customer, string?>("Customer.FirstName", "First name", FieldType.String, c => c.FirstName),
            Col<Customer, string?>("Customer.LastName", "Last name", FieldType.String, c => c.LastName),
            Col<Customer, string?>("Customer.EmailAddress", "Email", FieldType.String, c => c.EmailAddress, isDefault: true, sensitive: true),
            Col<Customer, string?>("Customer.MobileNumber", "Mobile", FieldType.String, c => c.MobileNumber, sensitive: true),
            Col<Customer, CustomerType>("Customer.CustomerType", "Customer type", FieldType.Enum, c => c.CustomerType, isDefault: true),
            Col<Customer, PloomType>("Customer.PloomType", "Customer Ploom type", FieldType.Enum, c => c.PloomType, isDefault: true),
            Col<Customer, string?>("Customer.Province", "Province", FieldType.String, c => c.Province!.Name, isDefault: true),
            Col<Customer, DateTime>("Customer.DateCreated", "Date created", FieldType.Date, c => c.DateCreated, isDefault: true),

            // Aggregates over the customer's matching transactions: live, and matching every
            // Transaction/Program filter of the report (the query builder supplies `ts`).
            Agg<Customer, int>("Customer.TransactionCount", "Total transactions", FieldType.Number,
                (c, ts) => ts.Count(), isDefault: true),
            Agg<Customer, DateTime?>("Customer.FirstTransactionDate", "First transaction date", FieldType.Date,
                (c, ts) => ts.Min(t => (DateTime?)t.StartTransactionDate)),
            Agg<Customer, DateTime?>("Customer.LastTransactionDate", "Last transaction date", FieldType.Date,
                (c, ts) => ts.Max(t => (DateTime?)t.StartTransactionDate), isDefault: true),
            Agg<Customer, int>("Customer.ProgramCount", "Program count", FieldType.Number,
                (c, ts) => ts.Select(t => t.ProgramId).Distinct().Count()),
            Agg<Customer, string>("Customer.ProgramNames", "Programs joined", FieldType.String,
                (c, ts) => string.Join(", ", ts.Select(t => t.Program!.ProgramName).Distinct()),
                sortable: false),
        };

        return new RootDefinition(ReportRoot.Customer, "Customers", columns,
            "Customer.DateCreated", SortDirection.Desc,
            new Dictionary<string, string>
            {
                [ReportEntities.Customer] = "Customer fields",
                [ReportEntities.Transaction] = "Has a transaction matching",
                [ReportEntities.Program] = "Has a transaction in a program matching",
            });
    }

    private static RootDefinition BuildProgramRoot()
    {
        var columns = new List<ColumnDefinition>
        {
            Col<Program, long>("Program.Id", "Program ID", FieldType.Number, p => p.Id),
            Col<Program, string?>("Program.ProgramName", "Program", FieldType.String, p => p.ProgramName, isDefault: true),
            Col<Program, ProgramType>("Program.ProgramType", "Program type", FieldType.Enum, p => p.ProgramType, isDefault: true),
            Col<Program, ProgramStatus>("Program.Status", "Status", FieldType.Enum, p => p.Status, isDefault: true),
            Col<Program, DateTime>("Program.StartDate", "Start date", FieldType.Date, p => p.StartDate, isDefault: true),
            Col<Program, DateTime>("Program.EndDate", "End date", FieldType.Date, p => p.EndDate, isDefault: true),
            Col<Program, string?>("Program.FocusZone", "Focus zone", FieldType.String, p => p.FocusZone, isDefault: true),

            // Aggregates over the program's matching transactions: live, and matching every
            // Transaction/Customer filter of the report (the query builder supplies `ts`).
            Agg<Program, int>("Program.TransactionCount", "Total transactions", FieldType.Number,
                (p, ts) => ts.Count(), isDefault: true),
            Agg<Program, int>("Program.CustomerCount", "Distinct customers", FieldType.Number,
                (p, ts) => ts.Select(t => t.CustomerId).Distinct().Count(), isDefault: true),
            // Outlets belong to the program itself, so this one is not scoped by transaction filters.
            Col<Program, int>("Program.OutletCount", "Outlet count", FieldType.Number,
                p => p.Outlets!.Count(o => !o.IsDeleted)),
            Agg<Program, DateTime?>("Program.FirstTransactionDate", "First transaction date", FieldType.Date,
                (p, ts) => ts.Min(t => (DateTime?)t.StartTransactionDate)),
            Agg<Program, DateTime?>("Program.LastTransactionDate", "Last transaction date", FieldType.Date,
                (p, ts) => ts.Max(t => (DateTime?)t.StartTransactionDate)),
        };

        return new RootDefinition(ReportRoot.Program, "Programs", columns,
            "Program.StartDate", SortDirection.Desc,
            new Dictionary<string, string>
            {
                [ReportEntities.Program] = "Program fields",
                [ReportEntities.Transaction] = "Has a transaction matching",
                [ReportEntities.Customer] = "Has a transaction by a customer matching",
            });
    }

    // ---------- Filters ----------

    private static List<FilterDefinition> BuildFilters() =>
    [
        Filter<Customer, string?>(ReportEntities.Customer, "FirstName", "Customer first name", FieldType.String, FilterOperators.String, c => c.FirstName),
        Filter<Customer, string?>(ReportEntities.Customer, "LastName", "Customer last name", FieldType.String, FilterOperators.String, c => c.LastName),
        Filter<Customer, string?>(ReportEntities.Customer, "EmailAddress", "Customer email", FieldType.String, FilterOperators.String, c => c.EmailAddress),
        Filter<Customer, DateTime>(ReportEntities.Customer, "DateCreated", "Customer date created", FieldType.Date, FilterOperators.Date, c => c.DateCreated),
        Filter<Customer, CustomerType>(ReportEntities.Customer, "CustomerType", "Customer type", FieldType.Enum, FilterOperators.Set, c => c.CustomerType),
        Filter<Customer, PloomType>(ReportEntities.Customer, "PloomType", "Customer Ploom type", FieldType.Enum, FilterOperators.Set, c => c.PloomType),
        Filter<Customer, string?>(ReportEntities.Customer, "ProvincePSGCCode", "Customer province", FieldType.Lookup, FilterOperators.Set, c => c.ProvincePSGCCode,
            lookup: LookupSources.Provinces),

        Filter<Transaction, DateTime>(ReportEntities.Transaction, "StartTransactionDate", "Transaction date", FieldType.Date, FilterOperators.Date, t => t.StartTransactionDate),
        Filter<Transaction, TransactionPloomType>(ReportEntities.Transaction, "PloomType", "Transaction Ploom type", FieldType.Enum, FilterOperators.Set, t => t.PloomType),
        Filter<Transaction, long>(ReportEntities.Transaction, "CustomerId", "Customer", FieldType.Lookup, FilterOperators.Set, t => t.CustomerId,
            lookup: LookupSources.Customers),
        Filter<Transaction, long>(ReportEntities.Transaction, "ProgramId", "Program", FieldType.Lookup, FilterOperators.Set, t => t.ProgramId,
            lookup: LookupSources.Programs),
        Filter<Transaction, long>(ReportEntities.Transaction, "ProgramOutletId", "Program outlet", FieldType.Lookup, FilterOperators.Set, t => t.ProgramOutletId,
            lookup: LookupSources.Outlets, dependsOn: "Transaction.ProgramId"),

        Filter<Program, string?>(ReportEntities.Program, "ProgramName", "Program name", FieldType.String, [FilterOperators.Eq, FilterOperators.Contains], p => p.ProgramName),
        Filter<Program, ProgramType>(ReportEntities.Program, "ProgramType", "Program type", FieldType.Enum, FilterOperators.Set, p => p.ProgramType),
        Filter<Program, ProgramStatus>(ReportEntities.Program, "Status", "Program status", FieldType.Enum, FilterOperators.Set, p => p.Status),
        Filter<Program, DateTime>(ReportEntities.Program, "StartDate", "Program start date", FieldType.Date, FilterOperators.Date, p => p.StartDate),
        Filter<Program, DateTime>(ReportEntities.Program, "EndDate", "Program end date", FieldType.Date, FilterOperators.Date, p => p.EndDate),
        Filter<Program, string?>(ReportEntities.Program, "FocusZone", "Program focus zone", FieldType.String, [FilterOperators.In, FilterOperators.Eq], p => p.FocusZone,
            lookup: LookupSources.FocusZones),
    ];

    private static ColumnDefinition Col<TRoot, TValue>(string key, string label, FieldType type,
        Expression<Func<TRoot, TValue>> selector, bool isDefault = false, bool sortable = true, bool sensitive = false) =>
        new(key, label, type, selector, isDefault, sortable, sensitive);

    /// <summary>
    /// An aggregate column. The second parameter is the root's <em>matching</em> transactions, which the
    /// query builder substitutes per report, so the aggregate always agrees with the rows' filters.
    /// </summary>
    private static ColumnDefinition Agg<TRoot, TValue>(string key, string label, FieldType type,
        Expression<Func<TRoot, IEnumerable<Transaction>, TValue>> selector, bool isDefault = false, bool sortable = true) =>
        new(key, label, type, selector, isDefault, sortable, sensitive: false);

    private static FilterDefinition Filter<TEntity, TValue>(string entity, string field, string label, FieldType type,
        IReadOnlyList<string> operators, Expression<Func<TEntity, TValue>> selector, string? lookup = null, string? dependsOn = null) =>
        new(entity, field, label, type, operators, selector, lookup, dependsOn);
}
