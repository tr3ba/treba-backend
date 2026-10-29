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
            Application.Abstractions.SmsDeliveryException => (503, exception.Message),
            Application.Abstractions.DeliveryUnavailableException => (503, exception.Message),
            KeyNotFoundException => (404, "Запис не знайдено."),
            DbUpdateConcurrencyException when context.Request.Path.StartsWithSegments("/api/telegram") => (409, "Запит уже оброблено або дані змінилися. Оновіть сторінку."),
            ArgumentException => (400, exception.Message),
            UserDomainException => (409, exception.Message),
            DbUpdateConcurrencyException => (409, "Data changed while the request was being processed. Reload it before saving."),
            Application.Common.BusinessRuleException => (409, exception.Message),
            InvalidOperationException => (400, exception.Message),
            UnauthorizedAccessException => (401, exception.Message),
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "42P01" } }
                or Npgsql.PostgresException { SqlState: "42P01" } => (503, "A database update is required before this operation."),
            DbUpdateException => (409, "Data conflicts with an existing record or references an unavailable entity."),
            IOException => (503, "Photo storage is unavailable. Check the connection and try again."),
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
