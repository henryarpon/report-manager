using System.Diagnostics;
using JTI.DEEP.Reports.Application.Common.Exceptions;
using JTI.DEEP.Reports.Application.Common.Models;

namespace JTI.DEEP.Reports.API.Middleware;

/// <summary>Maps exceptions to HTTP responses (pattern from JTI-DEEP-API): OperationalException → 422.</summary>
public sealed class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationalException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            await context.Response.WriteAsJsonAsync(ex.Error);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away (e.g. cancelled a download). Nothing to report.
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ErrorResponse
            {
                Message = "An unexpected error occurred.",
                ReferenceKey = Activity.Current?.RootId ?? context.TraceIdentifier,
            });
        }
    }
}
