using System.Text.RegularExpressions;

namespace JTI.DEEP.Reports.Application.Reports.Catalog;

public static partial class EnumOptions
{
    /// <summary>Options generated from the C# enum so the frontend never hardcodes them.</summary>
    public static IReadOnlyList<EnumOption> For(Type enumType) =>
        Enum.GetValues(enumType)
            .Cast<object>()
            .Select(v => new EnumOption(Convert.ToInt32(v), Humanize(Enum.GetName(enumType, v)!)))
            .ToList();

    private static string Humanize(string name) => WordBoundary().Replace(name, " $1");

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex WordBoundary();
}
