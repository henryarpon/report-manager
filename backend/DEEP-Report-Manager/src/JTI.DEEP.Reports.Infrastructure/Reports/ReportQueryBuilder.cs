using System.Linq.Expressions;
using System.Reflection;
using JTI.DEEP.Reports.Application.Reports.Catalog;
using JTI.DEEP.Reports.Application.Reports.Definitions;
using JTI.DEEP.Reports.Domain.Entities;

namespace JTI.DEEP.Reports.Infrastructure.Reports;

/// <summary>
/// Turns a resolved (whitelisted) report definition into LINQ expression trees that EF Core translates to SQL.
/// No raw field names or SQL fragments ever reach the query: every selector comes from the catalog.
/// </summary>
/// <remarks>
/// For Customer and Program roots, the filters on other entities are combined into one
/// "matching transaction" predicate: a row is included when <em>one</em> live transaction matches all of
/// them together (a single EXISTS), and aggregate columns are computed over exactly those transactions.
/// </remarks>
internal static class ReportQueryBuilder
{
    private static readonly MethodInfo OrderByMethod = QueryableMethod(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescendingMethod = QueryableMethod(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo StringContains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
    private static readonly MethodInfo StringStartsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;

    public static IQueryable<TRoot> Filter<TRoot>(IQueryable<TRoot> query, ResolvedReport report) where TRoot : BaseEntity =>
        Filter(query, report, TransactionScope.For(report));

    /// <summary>Filtered, deterministically ordered (sort column + Id tiebreaker), optionally paged rows.</summary>
    public static IQueryable<object?[]> Rows<TRoot>(IQueryable<TRoot> query, ResolvedReport report, int? skip = null, int? take = null)
        where TRoot : BaseEntity
    {
        var scope = TransactionScope.For(report);
        var ordered = Order(Filter(query, report, scope), report, scope);

        IQueryable<TRoot> paged = ordered;
        if (skip is > 0)
        {
            paged = paged.Skip(skip.Value);
        }

        if (take is not null)
        {
            paged = paged.Take(take.Value);
        }

        return paged.Select(Projection<TRoot>(report.Columns, scope));
    }

    private static IQueryable<TRoot> Filter<TRoot>(IQueryable<TRoot> query, ResolvedReport report, TransactionScope? scope)
        where TRoot : BaseEntity
    {
        foreach (var filter in report.Filters.Where(f => !IsRelated(report.Root.Root, f)))
        {
            query = query.Where(DirectPredicate<TRoot>(report.Root.Root, filter));
        }

        if (scope?.Predicate is not null)
        {
            // One EXISTS: a single live transaction must match every related filter together.
            var x = Expression.Parameter(typeof(TRoot), "x");
            var any = Expression.Call(typeof(Enumerable), nameof(Enumerable.Any), [typeof(Transaction)],
                Expression.Property(x, scope.Collection), scope.Predicate);
            query = query.Where(Expression.Lambda<Func<TRoot, bool>>(any, x));
        }

        return query;
    }

    // ---------- Matching-transaction scope (Customer and Program roots) ----------

    /// <summary>
    /// The root's matching transactions: live, and matching every filter on an entity other than the root.
    /// Shared by the row filter (EXISTS) and the aggregate columns so both always agree.
    /// </summary>
    /// <param name="Predicate">The related filters AND-ed together; null when there are none.</param>
    private sealed record TransactionScope(string Collection, Expression<Func<Transaction, bool>>? Predicate)
    {
        public static TransactionScope? For(ResolvedReport report)
        {
            var collection = report.Root.Root switch
            {
                ReportRoot.Customer => nameof(Customer.Transactions),
                ReportRoot.Program => nameof(Program.Transactions),
                _ => null,
            };

            if (collection is null)
            {
                return null;
            }

            // Live = the transaction itself is not deleted (global query filter on the collection);
            // a deleted program or customer on the other side does not hide it.
            var t = Expression.Parameter(typeof(Transaction), "t");
            Expression? body = null;
            foreach (var filter in report.Filters.Where(f => IsRelated(report.Root.Root, f)))
            {
                var condition = Inline(EntityPredicate(filter), Navigate(t, filter.Definition.Entity));
                body = body is null ? condition : Expression.AndAlso(body, condition);
            }

            return new TransactionScope(collection, body is null ? null : Expression.Lambda<Func<Transaction, bool>>(body, t));
        }

        /// <summary>
        /// <c>root.Transactions.Where(matching)</c>, the value of an aggregate's second parameter
        /// (the bare collection when there are no related filters).
        /// </summary>
        public Expression Transactions(Expression root)
        {
            var transactions = Expression.Property(root, Collection);
            return Predicate is null
                ? transactions
                : Expression.Call(typeof(Enumerable), nameof(Enumerable.Where), [typeof(Transaction)], transactions, Predicate);
        }

        private static Expression Navigate(ParameterExpression transaction, string entity) => entity switch
        {
            ReportEntities.Transaction => transaction,
            ReportEntities.Customer => Expression.Property(transaction, nameof(Transaction.Customer)),
            ReportEntities.Program => Expression.Property(transaction, nameof(Transaction.Program)),
            _ => throw new InvalidOperationException($"Filter entity '{entity}' cannot be reached from a transaction."),
        };
    }

    /// <summary>A filter on another entity of a Customer/Program root: it goes into the matching-transaction scope.</summary>
    private static bool IsRelated(ReportRoot root, ParsedFilter filter) =>
        root != ReportRoot.Transaction && filter.Definition.Entity != root.ToString();

    // ---------- Columns ----------

    /// <summary>The column value for <paramref name="root"/>; aggregates get the matching transactions.</summary>
    private static Expression ColumnBody(ColumnDefinition column, Expression root, TransactionScope? scope)
    {
        if (!column.IsAggregate)
        {
            return Inline(column.Selector, root);
        }

        if (scope is null)
        {
            throw new InvalidOperationException($"Aggregate column '{column.Key}' needs a Customer or Program root.");
        }

        var parameters = column.Selector.Parameters;
        return new ParameterReplacer(new Dictionary<ParameterExpression, Expression>
        {
            [parameters[0]] = root,
            [parameters[1]] = scope.Transactions(root),
        }).Visit(column.Selector.Body);
    }

    // ---------- Ordering ----------

    private static IOrderedQueryable<TRoot> Order<TRoot>(IQueryable<TRoot> query, ResolvedReport report, TransactionScope? scope)
        where TRoot : BaseEntity
    {
        var x = Expression.Parameter(typeof(TRoot), "x");
        var body = ColumnBody(report.SortColumn, x, scope);
        var key = Expression.Lambda(body, x);
        var method = (report.SortDirection == SortDirection.Desc ? OrderByDescendingMethod : OrderByMethod)
            .MakeGenericMethod(typeof(TRoot), body.Type);

        var ordered = (IOrderedQueryable<TRoot>)method.Invoke(null, [query, key])!;

        // Id tiebreaker so pages never skip or repeat rows.
        return report.SortDirection == SortDirection.Desc
            ? ordered.ThenByDescending(r => r.Id)
            : ordered.ThenBy(r => r.Id);
    }

    // ---------- Projection ----------

    private static Expression<Func<TRoot, object?[]>> Projection<TRoot>(IReadOnlyList<ColumnDefinition> columns, TransactionScope? scope)
    {
        var root = Expression.Parameter(typeof(TRoot), "x");
        var values = columns.Select(column =>
        {
            var body = ColumnBody(column, root, scope);

            // Values read through an optional join (e.g. a deleted outlet) can be NULL even when the
            // CLR property is a non-nullable value type, so widen to Nullable<T> before boxing.
            if (body.Type.IsValueType && Nullable.GetUnderlyingType(body.Type) is null)
            {
                body = Expression.Convert(body, typeof(Nullable<>).MakeGenericType(body.Type));
            }

            return (Expression)Expression.Convert(body, typeof(object));
        });

        return Expression.Lambda<Func<TRoot, object?[]>>(Expression.NewArrayInit(typeof(object), values), root);
    }

    // ---------- Filters ----------

    /// <summary>
    /// A filter applied straight to the root: its own fields, or (Transaction root) the customer/program
    /// reached through the many-to-one navigations.
    /// </summary>
    private static Expression<Func<TRoot, bool>> DirectPredicate<TRoot>(ReportRoot root, ParsedFilter filter)
    {
        var predicate = EntityPredicate(filter);
        var x = Expression.Parameter(typeof(TRoot), "x");

        var body = (root, filter.Definition.Entity) switch
        {
            (ReportRoot.Transaction, ReportEntities.Transaction) or
            (ReportRoot.Customer, ReportEntities.Customer) or
            (ReportRoot.Program, ReportEntities.Program) => Inline(predicate, x),

            (ReportRoot.Transaction, ReportEntities.Customer) => Inline(predicate, Expression.Property(x, nameof(Transaction.Customer))),
            (ReportRoot.Transaction, ReportEntities.Program) => Inline(predicate, Expression.Property(x, nameof(Transaction.Program))),

            _ => throw new InvalidOperationException($"Filter entity '{filter.Definition.Entity}' is not direct on the {root} root."),
        };

        return Expression.Lambda<Func<TRoot, bool>>(body, x);
    }

    /// <summary>Builds entity => bool for one filter, with values passed as SQL parameters.</summary>
    private static LambdaExpression EntityPredicate(ParsedFilter filter)
    {
        var selector = filter.Definition.Selector;
        var field = selector.Body;

        Expression condition = (filter.Op, filter.Value) switch
        {
            (FilterOperators.Eq, string text) => Expression.Equal(field, Parameter(text, field.Type)),
            (FilterOperators.Contains, string text) => Expression.Call(field, StringContains, Parameter(text, typeof(string))),
            (FilterOperators.StartsWith, string text) => Expression.Call(field, StringStartsWith, Parameter(text, typeof(string))),
            (FilterOperators.In, Array values) => Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [field.Type],
                Parameter(values, field.Type.MakeArrayType()), field),
            (FilterOperators.Between or FilterOperators.Before or FilterOperators.After, DateRange range) => DateCondition(field, range),
            _ => throw new InvalidOperationException($"Operator '{filter.Op}' cannot be applied to {filter.Definition.Key}."),
        };

        return Expression.Lambda(condition, selector.Parameters);
    }

    private static Expression DateCondition(Expression field, DateRange range)
    {
        Expression? condition = null;
        if (range.From is { } from)
        {
            condition = Expression.GreaterThanOrEqual(field, Parameter(from, field.Type));
        }

        if (range.ToExclusive is { } to)
        {
            var upper = Expression.LessThan(field, Parameter(to, field.Type));
            condition = condition is null ? upper : Expression.AndAlso(condition, upper);
        }

        return condition ?? Expression.Constant(true);
    }

    /// <summary>
    /// Wraps a value so EF Core sends it as a SQL parameter (as it does for captured variables)
    /// instead of inlining it as a literal.
    /// </summary>
    private static Expression Parameter(object value, Type type)
    {
        var holder = Activator.CreateInstance(typeof(ValueHolder<>).MakeGenericType(type), value)!;
        return Expression.Property(Expression.Constant(holder), nameof(ValueHolder<object>.Value));
    }

    private sealed class ValueHolder<T>(T value)
    {
        public T Value { get; } = value;
    }

    // ---------- Expression helpers ----------

    /// <summary>Substitutes the lambda's single parameter with <paramref name="argument"/> and returns the body.</summary>
    private static Expression Inline(LambdaExpression lambda, Expression argument) =>
        new ParameterReplacer(new Dictionary<ParameterExpression, Expression> { [lambda.Parameters[0]] = argument })
            .Visit(lambda.Body);

    private sealed class ParameterReplacer(IReadOnlyDictionary<ParameterExpression, Expression> replacements) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            replacements.TryGetValue(node, out var replacement) ? replacement : base.VisitParameter(node);
    }

    private static MethodInfo QueryableMethod(string name) =>
        typeof(Queryable).GetMethods().Single(m => m.Name == name && m.GetParameters().Length == 2);
}
