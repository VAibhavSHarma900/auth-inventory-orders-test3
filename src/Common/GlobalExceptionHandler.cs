using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Npgsql;

namespace Test3.Common;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Client disconnected before request completion");
            context.Response.StatusCode = 499;
            return true;
        }
        var status = exception is ApiException api ? api.StatusCode : 500;
        var detail = exception is ApiException ? exception.Message : "An unexpected error occurred. Refer to the correlation ID.";
        if (status >= 500)
            logger.LogError("Request failed: status {Status}, type {ExceptionType}, SQL state {SqlState}, stack {Stack}",
                status, exception.GetType().Name, (exception as PostgresException)?.SqlState, exception.StackTrace);
        else logger.LogWarning("Request rejected: status {Status}, reason {Reason}", status, detail);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails
        { Status = status, Title = ReasonPhrases.GetReasonPhrase(status), Detail = detail, Instance = context.Request.Path };
        problem.Extensions["correlationId"] = context.TraceIdentifier;
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
        return true;
    }
}
