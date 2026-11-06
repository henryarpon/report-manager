using JTI.DEEP.Reports.Application.Reports.Abstractions;
using JTI.DEEP.Reports.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JTI.DEEP.Reports.Infrastructure.Reports;

public sealed class EfLookupReader(ReportDbContext db) : ILookupReader
{
    public async Task<IReadOnlyList<LookupItem>> CustomersAsync(string search, int take, CancellationToken cancellationToken)
    {
        // Deleted customers are included on purpose: their transactions still appear in Transaction reports
        // and must stay filterable. JTI-DEEP-API does not rename them, so the label says "(deleted)".
        var customers = await db.Customers
            .Where(c => c.EmailAddress!.Contains(search)
                        || c.FirstName!.Contains(search)
                        || c.LastName!.Contains(search)
                        || c.FullName!.Contains(search))
            .OrderBy(c => c.FullName)
            .ThenBy(c => c.Id)
            .Take(take)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.FullName, c.EmailAddress, c.IsDeleted })
            .ToListAsync(cancellationToken);

        return customers
            .Select(c =>
            {
                var name = $"{c.FirstName} {c.LastName}".Trim();
                if (name.Length == 0)
                {
                    name = c.FullName ?? $"Customer #{c.Id}";
                }

                var label = string.IsNullOrWhiteSpace(c.EmailAddress) ? name : $"{name} ({c.EmailAddress})";
                return new LookupItem(c.Id, c.IsDeleted ? label + " (deleted)" : label);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<LookupItem>> ProgramsAsync(string search, int take, CancellationToken cancellationToken) =>
        await db.Programs
            // Deleted programs are included on purpose: their transactions still appear in reports, and
            // JTI-DEEP-API renames a deleted program to "<name> - (Deleted at …)", so the label says so.
            .Where(p => p.ProgramName!.Contains(search))
            .OrderBy(p => p.ProgramName)
            .ThenBy(p => p.Id)
            .Take(take)
            .Select(p => new LookupItem(p.Id, p.ProgramName ?? "Program #" + p.Id, null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> OutletsAsync(IReadOnlyCollection<long> programIds, string? search, int take,
        CancellationToken cancellationToken)
    {
        // Deleted outlets are excluded: transactions show empty outlet columns for them.
        var query = db.ProgramOutlets.Live().Where(o => programIds.Contains(o.ProgramId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o => o.OutletName!.Contains(search) || o.OutletCode!.Contains(search));
        }

        return await query
            .OrderBy(o => o.Program!.ProgramName)
            .ThenBy(o => o.OutletName)
            .ThenBy(o => o.Id)
            .Take(take)
            .Select(o => new LookupItem(o.Id, (o.OutletName ?? o.OutletCode ?? "Outlet") + " — " + o.Program!.ProgramName, o.ProgramId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LookupItem>> ProvincesAsync(CancellationToken cancellationToken) =>
        await db.Provinces
            .OrderBy(p => p.Name)
            .Select(p => new LookupItem(p.PSGCCode, p.Name, null))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LookupItem>> FocusZonesAsync(CancellationToken cancellationToken)
    {
        // Deleted programs' zones are included, like the program lookup: their transactions stay in reports.
        var zones = await db.Programs
            .Where(p => p.FocusZone != null && p.FocusZone != "")
            .Select(p => p.FocusZone!)
            .Distinct()
            .OrderBy(z => z)
            .ToListAsync(cancellationToken);

        return zones.Select(z => new LookupItem(z, z)).ToList();
    }
}
