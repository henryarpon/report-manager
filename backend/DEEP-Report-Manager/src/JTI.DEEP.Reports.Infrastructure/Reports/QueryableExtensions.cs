using JTI.DEEP.Reports.Domain.Entities;

namespace JTI.DEEP.Reports.Infrastructure.Reports;

internal static class QueryableExtensions
{
    /// <summary>
    /// Excludes soft-deleted rows. Needed for Customer, Program and ProgramOutlet, which have no global
    /// soft-delete filter (see ReportDbContext), wherever they are listed themselves.
    /// </summary>
    public static IQueryable<T> Live<T>(this IQueryable<T> query) where T : BaseEntity =>
        query.Where(e => !e.IsDeleted);
}
