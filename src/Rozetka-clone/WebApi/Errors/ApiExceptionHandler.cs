using Domain.Entities.Users;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace WebApi.Errors;

public sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger
) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken ct
    )
    {
        var (status, title) = exception switch
        {
            ArgumentException => (400, exception.Message),
            UserDomainException => (409, exception.Message),
            Application.Common.BusinessRuleException => (409, exception.Message),
            InvalidOperationException => (400, exception.Message),
            UnauthorizedAccessException => (401, exception.Message),
            DbUpdateException => (409, "Data conflicts with an existing record or references an unavailable entity."),
            _ => (500, "An unexpected error occurred."),
        };
        logger.LogError(
            exception,
            "Request failed with status {Status}; trace {TraceId}",
            status,
            context.TraceIdentifier
        );
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Instance = context.Request.Path,
                Extensions = {
                    ["traceId"] = context.TraceIdentifier
                },
            },
            ct
        );
        return true;
    }
}
