using System.Linq.Expressions;

namespace JTI.DEEP.Reports.Application.Reports.Catalog;

/// <summary>
/// A selectable report column. <see cref="Selector"/> is a lambda from the root entity to the value
/// and is translated to SQL by the query builder. Aggregate columns take a second parameter, the
/// root's matching transactions (<see cref="IsAggregate"/>), which the query builder supplies.
/// </summary>
public sealed class ColumnDefinition
{
    private readonly Dictionary<int, string>? _optionLabels;

    public ColumnDefinition(string key, string label, FieldType type, LambdaExpression selector,
        bool isDefault, bool sortable, bool sensitive)
    {
        Key = key;
        Label = label;
        Type = type;
        Selector = selector;
        IsDefault = isDefault;
        Sortable = sortable;
        Sensitive = sensitive;

        var valueType = Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType;
        if (valueType.IsEnum)
        {
            _optionLabels = EnumOptions.For(valueType).ToDictionary(o => o.Value, o => o.Label);
        }
    }

    public string Key { get; }
    public string Label { get; }
    public FieldType Type { get; }
    public LambdaExpression Selector { get; }

    /// <summary>The selector is <c>(root, matchingTransactions) =&gt; value</c>.</summary>
    public bool IsAggregate => Selector.Parameters.Count == 2;
    public bool IsDefault { get; }
    public bool Sortable { get; }

    /// <summary>PII marker for future per-role column rules. Not enforced in the MVP.</summary>
    public bool Sensitive { get; }

    /// <summary>Turns raw values into display values (enum labels). Shared by the table and the CSV.</summary>
    public object? Format(object? value) =>
        value is not null && _optionLabels is not null && _optionLabels.TryGetValue(Convert.ToInt32(value), out var label)
            ? label
            : value;
}
