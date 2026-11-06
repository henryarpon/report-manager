using FluentValidation;
using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using MediatR;
using Microsoft.Extensions.Options;

namespace JTI.DEEP.Reports.Application.Reports.Queries.Lookup;

/// <summary>Typeahead / option lists for lookup filters.</summary>
public sealed record LookupQuery(string Entity, string? Search, int? Take, IReadOnlyList<long>? ProgramIds)
    : IRequest<IReadOnlyList<LookupItem>>;

public sealed class LookupQueryValidator : AbstractValidator<LookupQuery>
{
    public LookupQueryValidator(IOptions<ReportOptions> options)
    {
        var settings = options.Value;

        RuleFor(x => x.Entity)
            .Must(e => LookupSources.All.Contains(e, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Unknown lookup. Use one of: {string.Join(", ", LookupSources.All)}.");

        RuleFor(x => x.Take).InclusiveBetween(1, settings.MaxLookupResults).When(x => x.Take is not null);
        RuleFor(x => x.Search).MaximumLength(100);

        // Customer search exposes PII: require a meaningful search term.
        RuleFor(x => x.Search)
            .Must(s => s?.Trim().Length >= settings.CustomerSearchMinLength)
            .WithMessage($"Type at least {settings.CustomerSearchMinLength} characters to search customers.")
            .When(x => Is(x, LookupSources.Customers));

        RuleFor(x => x.Search)
            .Must(s => !string.IsNullOrWhiteSpace(s))
            .WithMessage("Type at least 1 character to search programs.")
            .When(x => Is(x, LookupSources.Programs));

        RuleFor(x => x.ProgramIds)
            .Must(ids => ids is { Count: > 0 and <= 100 })
            .WithMessage("Select at least one program (at most 100) before choosing outlets.")
            .When(x => Is(x, LookupSources.Outlets));
    }

    private static bool Is(LookupQuery query, string source) =>
        string.Equals(query.Entity, source, StringComparison.OrdinalIgnoreCase);
}

public sealed class LookupQueryHandler(ILookupReader reader, IOptions<ReportOptions> options)
    : IRequestHandler<LookupQuery, IReadOnlyList<LookupItem>>
{
    public Task<IReadOnlyList<LookupItem>> Handle(LookupQuery request, CancellationToken cancellationToken)
    {
        var take = Math.Min(request.Take ?? options.Value.MaxLookupResults, options.Value.MaxLookupResults);
        var search = request.Search?.Trim() ?? string.Empty;

        return request.Entity.ToLowerInvariant() switch
        {
            LookupSources.Customers => reader.CustomersAsync(search, take, cancellationToken),
            LookupSources.Programs => reader.ProgramsAsync(search, take, cancellationToken),
            LookupSources.Outlets => reader.OutletsAsync(request.ProgramIds!.Distinct().ToList(), search, take, cancellationToken),
            LookupSources.Provinces => reader.ProvincesAsync(cancellationToken),
            LookupSources.FocusZones => reader.FocusZonesAsync(cancellationToken),
            _ => throw new InvalidOperationException($"Unhandled lookup '{request.Entity}'."),
        };
    }
}
