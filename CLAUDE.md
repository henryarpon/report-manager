# CLAUDE.md

Project guidance for the **Reports Management & Dynamic Filters** feature of JTI.DEEP.

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 (ASP.NET Core Web API), C#, MediatR, FluentValidation, CsvHelper |
| Architecture | Clean Architecture + CQRS |
| Frontend | Angular 22 (standalone, signals) + Angular Material/CDK |
| Database | Microsoft SQL Server (MSSQL), local instance for development (no Docker) |
| ORM | Entity Framework Core 10 |

## Repository layout

| Path | Contents |
|---|---|
| `backend/DEEP-Report-Manager/` | Solution `DEEP-Report-Manager.slnx` with `src/JTI.DEEP.Reports.{Domain,Application,Infrastructure,API}` |
| `frontend/deep-report-manager/` | Angular app (`/reports` page). Dev server proxies `/api` to the backend |

### Relationship to JTI-DEEP-API

This is a **standalone, read-only** service. The schema is owned by the main JTI-DEEP-API repo (`DEEP-Project\DEEP-JTI-REPOS\DEEP-API\JTI-DEEP-API`).

- `ReportDbContext` maps only the tables reports need (`Customers`, `Transactions`, `Programs`, `ProgramOutlets`, `Provinces`) with trimmed entity copies.
- **Never add migrations here.** If the JTI-DEEP-API schema changes, update the trimmed entities/enums by hand.
- **Do not touch existing databases.** Local dev uses its own mock database `DeepReportsMock` on the local default SQL Server instance. In Development, when `MockData:Enabled` is true, the API creates it (`EnsureCreated`, same table/column names as JTI-DEEP-API) and fills it with generated mock data (`MockDataSeeder`). Delete the database to regenerate.

## Architecture

Dependencies point inward only: API → Infrastructure/Application → Domain.

| Layer | Responsibility for this feature |
|---|---|
| **Domain** | Trimmed entity copies and enums only. No reporting logic. |
| **Application** | Report definition models, field catalog, FluentValidation validators, MediatR queries and handlers, `IReportReader` / `IReportFileWriter` abstractions, response DTOs |
| **Infrastructure** | `ReportDbContext`, EF Core query builder (expression trees) implementing `IReportReader`, CSV/ZIP writing |
| **API** | Thin controller that sends MediatR requests and returns results. No business logic. |

### CQRS

- The feature is **read-only**; MVP has **queries only**: `GetReportCatalogQuery`, `LookupQuery`, `RunReportQuery` (paged), `ExportReportQuery` (streamed).
- Commands arrive later, with saved reports.
- Queries run `AsNoTracking()` and project straight to rows/DTOs. Never return domain entities.
- Validation runs in the MediatR `ValidationBehavior` (FluentValidation → 422), not in controllers.
- `RunReportQuery` and `ExportReportQuery` share **one** query builder so the table and the CSV always match.

## Domain Model

```
Customer 1 ──< Transaction >── 1 Program ──< ProgramOutlet
                    │                            ▲
                    └──────── 1 ─────────────────┘ (Transaction.Outlet)
Customer.ProvincePSGCCode ──> Province.PSGCCode (alternate key)
```

- **Transaction is the hub.** "Customers in a program" means "customers who have a transaction in that program."
- Real property names (differ from older docs): `Program.Status` (enum `ProgramStatus`), navigation `Transaction.Outlet` (FK `ProgramOutletId`), Province link is the **PSGC code string**, not a numeric id.
- `BaseEntity`: `Id`, `DateCreated`, `IsDeleted`, … (only these are mapped here).

Relevant fields:

- **Customer:** `FirstName`, `LastName`, `FullName`, `EmailAddress` (not unique, not indexed), `MobileNumber`, `CustomerType`, `PloomType`, `ProvincePSGCCode` / `Province`, `DateCreated`
- **Transaction:** `StartTransactionDate`, `EndTransactionDate`, `PloomType` (`TransactionPloomType`), `CustomerId`, `ProgramId`, `ProgramOutletId`
- **Program:** `ProgramName`, `ProgramType` (enum — "program type" means this, not the free-text `Types`), `Status`, `StartDate`, `EndDate`, `FocusZone` (string)
- **ProgramOutlet:** `OutletName`, `OutletCode`, `City`, `Province` (plain text), `ProgramId`

## Report Model

A report is a definition, never one code path per combination:

| Part | Meaning |
|---|---|
| **Root** | What one row represents: `Transaction`, `Customer`, or `Program` |
| **Columns** | Catalog column keys (empty = root's default set) |
| **Filters** | Catalog filters, all combined with AND |
| **Sort** | `{ column, dir }` on a sortable column; `Id` is always the tiebreaker |

### Roots, grain and filter semantics

| Root | One row = | Filters on other entities |
|---|---|---|
| Transaction | one transaction (+ customer, program, outlet columns) | Applied through the many-to-one navigations |
| Customer | one customer | **All** Transaction/Program filters combine into **one** `EXISTS`: one live transaction must match them all ("has a transaction matching…") |
| Program | one program | **All** Transaction/Customer filters combine into **one** `EXISTS` |

**Same-transaction semantics** (decided 2026-11-05, replacing the earlier independent `EXISTS` per filter): "Program in X" + "date in Q1" matches a customer with *an X transaction in Q1*, not one with some X transaction and some other Q1 transaction. These are the root's **matching transactions**: live, and matching every filter on an entity other than the root. Filters on the root's own entity apply to the root directly.

### Aggregate columns

Customer and Program roots may show aggregate columns (correlated subqueries). Aggregates are computed over the root's **matching transactions**, the same set the row `EXISTS` uses, so a filtered row never shows 0 and the counts agree across roots (e.g. customers' Total transactions sum to the Transaction report's row count). With no related filters, that is all live transactions. Exception: Program "Outlet count" counts the program's live outlets and ignores transaction filters.

In `ReportCatalog`, aggregates are declared with `Agg` as `(root, ts) => …`; `ReportQueryBuilder` substitutes `ts` with `root.Transactions.Where(matching)`. Sorting by an aggregate uses the same scope.

- **Customer:** Total transactions, First/Last transaction date, Program count, Program names (`STRING_AGG`, not sortable)
- **Program:** Total transactions, Distinct customers, Outlet count, First/Last transaction date

### Soft deletes

A transaction is **live** when it is not deleted itself (decided 2026-11-05). A deleted customer, program or outlet does **not** hide its transactions: they still appear in Transaction rows (with that customer's/program's columns), in `EXISTS` and in aggregates.

Why: in JTI-DEEP-API, `DeleteProgramCommand` soft-deletes only the program. It never cascades to transactions and renames the program to `"<name> - (Deleted at <time>)"`, so the name labels itself. The app has no customer delete at all. Deleting a parent retires it; it does not invalidate its transactions. If a customer removal/anonymization flow is added later, revisit showing deleted customers' details (with the deferred `Sensitive`/roles work).

- The global `IsDeleted == false` query filter applies to `Transaction` and `Province` only. `Customer`, `Program` and `ProgramOutlet` deliberately have **no** global filter: they sit behind the required navigations `Transaction.Customer/Program/Outlet`, where a filter becomes an INNER JOIN that drops those transactions from the rows but not the count.
- The Customer and Program roots (`EfReportReader`) exclude deleted customers/programs with the `Live()` helper (`Reports/QueryableExtensions.cs`), as does the outlet lookup. Use it for roots and for outlets; the other lookups deliberately include deleted parents (next bullet).
- The **lookups include deleted parents**, because their transactions stay in Transaction reports as records of events and must stay filterable: the customer lookup (label suffixed " (deleted)", since JTI-DEEP-API does not rename customers), the program lookup (the name already says "(Deleted at …)") and the focus-zone lookup (zones of deleted programs).
- Deleted outlets show empty outlet columns and are excluded from the outlet lookup and Outlet count.
- Accepted for now: on the Program root, Customer filters inside the `EXISTS` and "Distinct customers" also match deleted customers.
- Consequence: the Customer report's Total transactions sum to the Transaction count *minus* transactions of deleted customers (likewise for programs).

### Example request

```json
{
  "root": "Transaction",
  "columns": ["Transaction.StartTransactionDate", "Customer.FullName", "Customer.EmailAddress", "Program.ProgramName"],
  "filters": [
    { "entity": "Customer",    "field": "ProvincePSGCCode",     "op": "in",      "value": ["072200000"] },
    { "entity": "Transaction", "field": "StartTransactionDate", "op": "between", "value": ["2026-01-01", "2026-03-31"] },
    { "entity": "Program",     "field": "Status",               "op": "in",      "value": [1] }
  ],
  "sort": { "column": "Transaction.StartTransactionDate", "dir": "desc" },
  "page": 1,
  "pageSize": 25
}
```

## Filter Catalog (MVP)

| Entity | Field | Type | Operators |
|---|---|---|---|
| Customer | FirstName, LastName, EmailAddress | string | eq, contains, startsWith |
| Customer | DateCreated | date | between, before, after |
| Customer | CustomerType, PloomType | enum | in |
| Customer | ProvincePSGCCode (shows `Province.Name`) | lookup | in |
| Transaction | StartTransactionDate | date | between, before, after |
| Transaction | PloomType (`TransactionPloomType`) | enum | in |
| Transaction | CustomerId, ProgramId, ProgramOutletId | lookup (id) | in |
| Program | ProgramName | string | eq, contains |
| Program | ProgramType, Status | enum | in |
| Program | StartDate, EndDate | date | between, before, after |
| Program | FocusZone | string | eq, in |

Label same-named fields clearly ("Customer Ploom type" vs "Transaction Ploom type").

**Deferred filters:** region, city, barangay, brand/BUMO, freebies, products, transaction status (does not exist), computed fields.

### Catalog rules

- One registry (`ReportCatalog`) drives the Angular pickers, backend validation and the query builder. Adding a field = one catalog entry.
- **Whitelist** only. Never pass raw field names or SQL fragments into queries.
- Enum options come from the C# enums via the catalog; the frontend never hardcodes them.
- Filters and columns are separate lists. Each root has a default column set. Columns carry an unused `Sensitive` flag for future role rules.

## Lookup Inputs (UI)

- **Customer:** typeahead; search ≥ 3 chars over email, first, last and full name; ≤ 20 results; label `First Last (email)`, suffixed " (deleted)" for deleted customers (included so their transactions can be filtered); sends ids.
- **Program:** typeahead; search ≥ 1 char; ≤ 20 results; includes deleted programs (labelled "(Deleted at …)" by JTI-DEEP-API).
- **Outlet:** enabled once ≥ 1 program is selected; options for all selected programs, labelled `Outlet — Program`; outlet picks are dropped when their program is removed.
- **Province:** multi-select; shows names, sends PSGC codes.
- **FocusZone:** options from distinct `Program.FocusZone`, including deleted programs' zones.

## API

All routes are under `/api/reports` (the Angular route `/reports` must not collide with the dev proxy).

| Endpoint | Purpose |
|---|---|
| `GET /api/reports/catalog` | Roots, columns, filters, operators, enum options |
| `GET /api/reports/lookups/{customers\|programs\|outlets\|provinces\|focus-zones}?search=&take=20&programIds=` | Lookup / typeahead |
| `POST /api/reports/run` | Body `{ root, columns, filters, sort, page, pageSize }` → `{ columns:[{key,label,type}], items: any[][], totalCount, page, pageSize }`. `pageSize` default 25, max 100 |
| `POST /api/reports/export` | Same body without paging. ≤ 50k rows → one `.csv`; > 50k → streamed `.zip` of `report_partN.csv` (50k rows each, header in each); > 500k → 422 |

Validation failures return **422** with `{ message, errors }`. Limits are configurable: `Reports:ExportChunkRows`, `Reports:MaxExportRows`.

## Implementation Rules

### Backend

- Build queries from the definition with expression trees over EF Core.
- Use `EXISTS` / `.Any()` for anything one-to-many. Never join collections into the main query.
- **Pagination:** deterministic `OrderBy` + `Id` tiebreaker; count query runs separately.
- **CSV:** stream with `IAsyncEnumerable` + CsvHelper; never buffer the result. CsvHelper escaping + injection escaping for cells starting with `=`, `+`, `-`, `@`.
- **Dates:** stored as Philippine local time (UTC+8), so no conversion. Ranges use inclusive end (`< end + 1 day`); `before X` = `< X`, `after X` = `≥ X + 1 day`.
- Validate every request against the catalog (known fields, valid operator for the type, value shape).

### Security and privacy (MVP)

- **No auth, roles, audit log or rate limiting in the MVP.** Customer typeahead still requires 3+ chars and caps results at 20.
- Deferred: per-role column whitelists (use the `Sensitive` flag), export audit log, rate limiting.

### Frontend

- Build the root picker, column picker and filter builder from the catalog.
- Server-side pagination and sorting only.
- Export button downloads the `.csv` / `.zip` returned by `/api/reports/export`.
- Follow `frontend/deep-report-manager/AGENTS.md` (signals, standalone, native control flow, WCAG AA).

## MVP Scope

1. Field catalog, three roots, AND-only filters, aggregates, sorting
2. Paged on-screen table
3. CSV export with 50k-row split and 500k ceiling

**Deferred:** saved reports, OR groups, per-filter "any transaction" (independent EXISTS) as an option, computed fields (BUMO), async exports, freebies/products/location filters, auth/roles/audit, automated tests (test seam when added: the HTTP API against local SQL Server).

## Open Questions

- [ ] Default column set per root: confirm with stakeholders.
- [ ] Shape of `TransactionFreebies` / `TransactionProducts` for the deferred filters (both have `Quantity` and `TransactionId`; no back-navigation on `Transaction`).

Resolved: `IsDeleted` is a global query filter; `EmailAddress` is not unique; "program type" = `ProgramType` enum; dates are stored as Manila local time.
