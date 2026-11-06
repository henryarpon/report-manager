using FluentValidation;

namespace JTI.DEEP.Reports.Application.Reports.Definitions;

/// <summary>Validates a definition against the catalog: known columns, filters, operators, value shapes, sort.</summary>
public abstract class ReportDefinitionValidator<T> : AbstractValidator<T> where T : IReportDefinition
{
    protected ReportDefinitionValidator(ReportResolver resolver)
    {
        RuleFor(x => x).Custom((definition, context) =>
        {
            var errors = new List<(string Property, string Message)>();
            resolver.TryResolve(definition, errors);
            foreach (var (property, message) in errors)
            {
                context.AddFailure(property, message);
            }
        });
    }
}
