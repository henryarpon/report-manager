using JTI.DEEP.Reports.Application.Reports.Catalog;
using MediatR;
using Microsoft.Extensions.Options;

namespace JTI.DEEP.Reports.Application.Reports.Queries.GetReportCatalog;

public sealed record GetReportCatalogQuery : IRequest<CatalogDto>;

public sealed record CatalogDto(IReadOnlyList<RootDto> Roots, IReadOnlyList<FilterDto> Filters, int DefaultPageSize, int MaxPageSize);

public sealed record RootDto(
    ReportRoot Key,
    string Label,
    IReadOnlyList<ColumnDto> Columns,
    SortDto DefaultSort,
    IReadOnlyDictionary<string, string> FilterScopes);

public sealed record ColumnDto(string Key, string Label, FieldType Type, bool Sortable, bool Default, bool Sensitive);

public sealed record SortDto(string Column, SortDirection Dir);

public sealed record FilterDto(
    string Key,
    string Entity,
    string Field,
    string Label,
    FieldType Type,
    IReadOnlyList<string> Operators,
    IReadOnlyList<EnumOption>? Options,
    string? Lookup,
    string? DependsOn);

public sealed class GetReportCatalogQueryHandler(ReportCatalog catalog, IOptions<ReportOptions> options)
    : IRequestHandler<GetReportCatalogQuery, CatalogDto>
{
    public Task<CatalogDto> Handle(GetReportCatalogQuery request, CancellationToken cancellationToken)
    {
        var roots = catalog.Roots.Values
            .Select(r => new RootDto(
                r.Root,
                r.Label,
                r.Columns.Select(c => new ColumnDto(c.Key, c.Label, c.Type, c.Sortable, c.IsDefault, c.Sensitive)).ToList(),
                new SortDto(r.DefaultSortColumn.Key, r.DefaultSortDirection),
                r.FilterScopes))
            .ToList();

        var filters = catalog.Filters
            .Select(f => new FilterDto(f.Key, f.Entity, f.Field, f.Label, f.Type, f.Operators, f.Options, f.Lookup, f.DependsOn))
            .ToList();

        return Task.FromResult(new CatalogDto(roots, filters, options.Value.DefaultPageSize, options.Value.MaxPageSize));
    }
}
