namespace JTI.DEEP.Reports.Application.Common.Models;

public sealed class ErrorResponse
{
    public required string Message { get; init; }
    public string? ReferenceKey { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}
