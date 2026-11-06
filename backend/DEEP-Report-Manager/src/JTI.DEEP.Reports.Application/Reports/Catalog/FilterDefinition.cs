using System.Linq.Expressions;

namespace JTI.DEEP.Reports.Application.Reports.Catalog;

/// <summary>
/// A filterable field on one entity. <see cref="Selector"/> is a lambda from that entity to the field;
/// the query builder applies it to whichever root is being reported on.
/// </summary>
public sealed class FilterDefinition
{
    public FilterDefinition(string entity, string field, string label, FieldType type,
        IReadOnlyList<string> operators, LambdaExpression selector, string? lookup = null, string? dependsOn = null)
    {
        Entity = entity;
        Field = field;
        Label = label;
        Type = type;
        Operators = operators;
        Selector = selector;
        Lookup = lookup;
        DependsOn = dependsOn;

        ValueType = Nullable.GetUnderlyingType(selector.ReturnType) ?? selector.ReturnType;
        Options = ValueType.IsEnum ? EnumOptions.For(ValueType) : null;
    }

    public string Key => $"{Entity}.{Field}";
    public string Entity { get; }
    public string Field { get; }
    public string Label { get; }
    public FieldType Type { get; }
    public IReadOnlyList<string> Operators { get; }
    public LambdaExpression Selector { get; }

    /// <summary>CLR type of the field (the enum type for enums).</summary>
    public Type ValueType { get; }
    public IReadOnlyList<EnumOption>? Options { get; }

    /// <summary>Lookup endpoint that supplies values (customers, programs, outlets, provinces, focus-zones).</summary>
    public string? Lookup { get; }

    /// <summary>Filter key this one cascades from (outlets depend on programs).</summary>
    public string? DependsOn { get; }
}
