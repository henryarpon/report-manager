using JTI.DEEP.Reports.Application.Common.Models;

namespace JTI.DEEP.Reports.Application.Common.Exceptions;

/// <summary>Request is well-formed but cannot be processed. Mapped to HTTP 422.</summary>
public sealed class OperationalException(ErrorResponse error) : Exception(error.Message)
{
    public ErrorResponse Error { get; } = error;

    public static OperationalException WithMessage(string message, string? property = null) =>
        new(new ErrorResponse
        {
            Message = message,
            Errors = property is null ? null : new Dictionary<string, string[]> { [property] = [message] },
        });
}
