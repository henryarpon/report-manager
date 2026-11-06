using System.Diagnostics;
using FluentValidation;
using JTI.DEEP.Reports.Application.Common.Exceptions;
using JTI.DEEP.Reports.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JTI.DEEP.Reports.Application.Common.Behaviors;

/// <summary>Mirrors JTI-DEEP-API's ValidationBehavior: FluentValidation failures become a 422 OperationalException.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));
            var failures = results.SelectMany(r => r.Errors).ToList();

            if (failures.Count != 0)
            {
                var errors = failures
                    .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
                    .ToDictionary(g => g.Key, g => g.ToArray());

                logger.LogWarning("Validation failed for {RequestName}: {@Errors}", typeof(TRequest).Name, errors);
                throw new OperationalException(new ErrorResponse
                {
                    Message = "One or more validation errors occurred.",
                    ReferenceKey = Activity.Current?.RootId,
                    Errors = errors,
                });
            }
        }

        return await next(cancellationToken);
    }
}
