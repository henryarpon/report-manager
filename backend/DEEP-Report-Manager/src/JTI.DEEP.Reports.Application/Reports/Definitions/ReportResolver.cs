using System.Globalization;
using System.Text.Json;
using JTI.DEEP.Reports.Application.Common.Exceptions;
using JTI.DEEP.Reports.Application.Common.Models;
using JTI.DEEP.Reports.Application.Reports.Catalog;

namespace JTI.DEEP.Reports.Application.Reports.Definitions;

/// <summary>
/// Matches a client definition against the catalog whitelist and parses filter values.
/// Used by the validators (to report errors) and by the handlers (to get the typed definition).
/// </summary>
public sealed class ReportResolver(ReportCatalog catalog)
{
    public const int MaxColumns = 50;
    public const int MaxFilters = 30;
    public const int MaxListValues = 500;

    private static readonly DateTime MinDate = new(1900, 1, 1);
    private static readonly DateTime MaxDate = new(2200, 1, 1);

    public ResolvedReport? TryResolve(IReportDefinition definition, ICollection<(string Property, string Message)> errors)
    {
        if (!catalog.Roots.TryGetValue(definition.Root, out var root))
        {
            errors.Add(("root", $"Unknown root. Use one of: {string.Join(", ", Enum.GetNames<ReportRoot>())}."));
            return null;
        }

        var columns = ResolveColumns(root, definition.Columns, errors);
        var filters = ResolveFilters(definition.Filters, errors);
        var (sortColumn, sortDir) = ResolveSort(root, definition.Sort, errors);

        return errors.Count == 0 ? new ResolvedReport(root, columns, filters, sortColumn!, sortDir) : null;
    }

    /// <summary>For handlers: the request already passed validation, so failures here are unexpected.</summary>
    public ResolvedReport Resolve(IReportDefinition definition)
    {
        var errors = new List<(string Property, string Message)>();
        return TryResolve(definition, errors) ?? throw new OperationalException(new ErrorResponse
        {
            Message = "One or more validation errors occurred.",
            Errors = errors.GroupBy(e => e.Property).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()),
        });
    }

    private static List<ColumnDefinition> ResolveColumns(RootDefinition root, IReadOnlyList<string>? keys,
        ICollection<(string, string)> errors)
    {
        if (keys is null || keys.Count == 0)
        {
            return root.DefaultColumns.ToList();
        }

        if (keys.Count > MaxColumns)
        {
            errors.Add(("columns", $"At most {MaxColumns} columns are allowed."));
        }

        var result = new List<ColumnDefinition>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
        {
            var column = root.FindColumn(keys[i] ?? string.Empty);
            if (column is null)
            {
                errors.Add(($"columns[{i}]", $"'{keys[i]}' is not a column of the {root.Root} report."));
            }
            else if (seen.Add(column.Key))
            {
                result.Add(column);
            }
        }

        return result;
    }

    private List<ParsedFilter> ResolveFilters(IReadOnlyList<ReportFilter>? filters, ICollection<(string, string)> errors)
    {
        var result = new List<ParsedFilter>();
        if (filters is null)
        {
            return result;
        }

        if (filters.Count > MaxFilters)
        {
            errors.Add(("filters", $"At most {MaxFilters} filters are allowed."));
            return result;
        }

        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var property = $"filters[{i}]";
            var definition = catalog.FindFilter(filter?.Entity, filter?.Field);
            if (filter is null || definition is null)
            {
                errors.Add((property, $"'{filter?.Entity}.{filter?.Field}' is not a filterable field."));
                continue;
            }

            var op = definition.Operators.FirstOrDefault(o => string.Equals(o, filter.Op, StringComparison.OrdinalIgnoreCase));
            if (op is null)
            {
                errors.Add(($"{property}.op",
                    $"Operator '{filter.Op}' is not valid for {definition.Label}. Use one of: {string.Join(", ", definition.Operators)}."));
                continue;
            }

            var value = ParseValue(definition, op, filter.Value, out var error);
            if (value is null)
            {
                errors.Add(($"{property}.value", $"{definition.Label}: {error}"));
                continue;
            }

            result.Add(new ParsedFilter(definition, op, value));
        }

        return result;
    }

    private static (ColumnDefinition?, SortDirection) ResolveSort(RootDefinition root, ReportSort? sort,
        ICollection<(string, string)> errors)
    {
        if (sort?.Column is null)
        {
            return (root.DefaultSortColumn, sort?.Dir ?? root.DefaultSortDirection);
        }

        var column = root.FindColumn(sort.Column);
        if (column is null || !column.Sortable)
        {
            errors.Add(("sort.column", $"'{sort.Column}' is not a sortable column of the {root.Root} report."));
            return (null, SortDirection.Asc);
        }

        return (column, sort.Dir ?? SortDirection.Asc);
    }

    // ---------- Value parsing ----------

    private static object? ParseValue(FilterDefinition definition, string op, JsonElement value, out string? error)
    {
        error = null;
        switch (op)
        {
            case FilterOperators.Eq or FilterOperators.Contains or FilterOperators.StartsWith:
                return ParseText(value, out error);

            case FilterOperators.Between:
                return ParseBetween(value, out error);

            case FilterOperators.Before:
                return ParseDate(value, out var before, out error) ? new DateRange(null, before) : null;

            case FilterOperators.After:
                return ParseDate(value, out var after, out error) ? new DateRange(after.AddDays(1), null) : null;

            case FilterOperators.In:
                return ParseList(definition, value, out error);

            default:
                error = $"Unsupported operator '{op}'.";
                return null;
        }
    }

    private static string? ParseText(JsonElement value, out string? error)
    {
        error = null;
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            var text = value.GetString()!.Trim();
            if (text.Length <= 200)
            {
                return text;
            }
        }

        error = "expected a non-empty text value of at most 200 characters.";
        return null;
    }

    private static DateRange? ParseBetween(JsonElement value, out string? error)
    {
        error = "expected [fromDate, toDate] as yyyy-MM-dd.";
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
        {
            return null;
        }

        if (!ParseDate(value[0], out var from, out error) || !ParseDate(value[1], out var to, out error))
        {
            return null;
        }

        if (from > to)
        {
            error = "the start date must not be after the end date.";
            return null;
        }

        // End date is inclusive: value < end + 1 day.
        return new DateRange(from, to.AddDays(1));
    }

    /// <summary>Dates are calendar days in Philippine local time, which is also how the database stores them.</summary>
    private static bool ParseDate(JsonElement value, out DateTime date, out string? error)
    {
        date = default;
        error = "expected a date as yyyy-MM-dd.";
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString() ?? string.Empty;
        if (text.Length >= 10 &&
            DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            date = day.ToDateTime(TimeOnly.MinValue);
            if (date >= MinDate && date < MaxDate)
            {
                error = null;
                return true;
            }
        }

        return false;
    }

    private static object? ParseList(FilterDefinition definition, JsonElement value, out string? error)
    {
        error = $"expected a non-empty array of at most {MaxListValues} values.";
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0 || value.GetArrayLength() > MaxListValues)
        {
            return null;
        }

        var items = value.EnumerateArray().ToList();

        if (definition.ValueType.IsEnum)
        {
            var allowed = definition.Options!.Select(o => o.Value).ToHashSet();
            var result = Array.CreateInstance(definition.ValueType, items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ValueKind != JsonValueKind.Number || !items[i].TryGetInt32(out var n) || !allowed.Contains(n))
                {
                    error = $"'{items[i]}' is not a valid option.";
                    return null;
                }

                result.SetValue(Enum.ToObject(definition.ValueType, n), i);
            }

            return result;
        }

        if (definition.ValueType == typeof(long))
        {
            var ids = new long[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ValueKind != JsonValueKind.Number || !items[i].TryGetInt64(out ids[i]))
                {
                    error = "expected numeric ids.";
                    return null;
                }
            }

            return ids;
        }

        if (definition.ValueType == typeof(string))
        {
            var texts = new string[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(items[i].GetString()))
                {
                    error = "expected non-empty text values.";
                    return null;
                }

                texts[i] = items[i].GetString()!;
            }

            return texts;
        }

        error = "unsupported value type.";
        return null;
    }
}
