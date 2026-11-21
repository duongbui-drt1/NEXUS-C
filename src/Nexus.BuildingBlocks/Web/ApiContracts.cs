using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Results;
using Npgsql;

namespace Nexus.BuildingBlocks.Web;

public sealed record ApiMeta(string CorrelationId);

public sealed record ApiResponse<T>(bool Success, T? Data, ApiMeta Meta)
{
    public static ApiResponse<T> Ok(T data, string correlationId)
        => new(true, data, new ApiMeta(correlationId));
}

public static class ApiResultMapper
{
    public static IActionResult ToActionResult<T>(this Result<T> result, HttpContext httpContext, int successStatusCode = StatusCodes.Status200OK)
    {
        var correlationId = GetCorrelationId(httpContext);
        if (result.IsSuccess)
        {
            var payload = ApiResponse<T>.Ok(result.Value, correlationId);
            return new ObjectResult(payload) { StatusCode = successStatusCode };
        }

        return ToProblemResult(result.Error, httpContext, correlationId);
    }

    public static IActionResult ToActionResult(this Result result, HttpContext httpContext, int successStatusCode = StatusCodes.Status204NoContent)
    {
        var correlationId = GetCorrelationId(httpContext);
        if (result.IsSuccess)
        {
            if (successStatusCode == StatusCodes.Status204NoContent)
            {
                return new NoContentResult();
            }

            return new ObjectResult(ApiResponse<object>.Ok(new { }, correlationId)) { StatusCode = successStatusCode };
        }

        return ToProblemResult(result.Error, httpContext, correlationId);
    }

    public static ObjectResult ToProblemResult(Error error, HttpContext httpContext, string? correlationId = null)
    {
        correlationId ??= GetCorrelationId(httpContext);
        var status = (int)error.Type;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = GetTitle(status),
            Detail = error.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://errors.nexus.internal/{error.Code.ToLowerInvariant().Replace('_', '-')}"
        };

        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["correlationId"] = correlationId;

        if (error.ValidationErrors is { Count: > 0 })
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static string GetCorrelationId(HttpContext context)
        => context.Items.TryGetValue(CorrelationIdMiddleware.ItemKey, out var value) && value is string cid
            ? cid
            : context.TraceIdentifier;

    private static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Entity",
        429 => "Too Many Requests",
        _ => "Internal Server Error"
    };
}

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var correlationId = ApiResultMapper.GetCorrelationId(httpContext);

        Error error;
        switch (exception)
        {
            case DomainException domainEx:
                error = domainEx.Error;
                logger.LogWarning("Domain error {ErrorCode}: {Message} (CorrelationId: {CorrelationId})",
                    error.Code, error.Message, correlationId);
                break;

            case DbUpdateConcurrencyException:
                error = Error.Conflict(
                    "CONCURRENCY_CONFLICT",
                    "The resource was modified by another concurrent operation. Please retry with the latest state.");
                logger.LogWarning("Optimistic concurrency conflict on {Path} (CorrelationId: {CorrelationId})",
                    httpContext.Request.Path, correlationId);
                break;

            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }:
                error = Error.Conflict(
                    "RESOURCE_CONFLICT",
                    "A resource with the same unique identifier or constraint already exists.");
                logger.LogWarning("Unique constraint violation on {Path} (CorrelationId: {CorrelationId})",
                    httpContext.Request.Path, correlationId);
                break;

            case FluentValidation.ValidationException validationEx:
                var grouped = validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.ErrorMessage).Distinct().ToArray());
                error = Error.Validation("VALIDATION_FAILED", "One or more validation errors occurred.", grouped);
                break;

            default:
                logger.LogError(exception, "Unhandled exception processing {Method} {Path} (CorrelationId: {CorrelationId})",
                    httpContext.Request.Method, httpContext.Request.Path, correlationId);
                error = Error.Failure("INTERNAL_SERVER_ERROR", "An unexpected error occurred while processing the request.");
                break;
        }

        var problem = new ProblemDetails
        {
            Status = (int)error.Type,
            Title = error.Type.ToString(),
            Detail = error.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://errors.nexus.internal/{error.Code.ToLowerInvariant().Replace('_', '-')}"
        };
        problem.Extensions["errorCode"] = error.Code;
        problem.Extensions["correlationId"] = correlationId;
        if (error.ValidationErrors is { Count: > 0 })
        {
            problem.Extensions["errors"] = error.ValidationErrors;
        }

        httpContext.Response.StatusCode = (int)error.Type;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

public static class SensitiveDataRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "passwd", "secret", "token", "accesstoken", "refreshtoken",
        "apikey", "api_key", "authorization", "credential", "privatekey", "clientsecret",
        "connectionstring", "hash", "salt", "otp", "mfa", "pin"
    ];

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return SensitiveKeyFragments.Any(f => normalized.Contains(f.Replace("_", string.Empty), StringComparison.Ordinal));
    }

    public static string? SanitizeToJson(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var node = value is string rawJson && rawJson.TrimStart().StartsWith('{')
            ? TryParseJson(rawJson)
            : JsonSerializer.SerializeToNode(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (node is null)
        {
            return null;
        }

        ScrubNode(node);
        return node.ToJsonString();
    }

    private static JsonNode? TryParseJson(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch
        {
            return JsonValue.Create("[UNPARSEABLE_REDACTED]");
        }
    }

    private static void ScrubNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                if (IsSensitiveKey(key))
                {
                    obj[key] = "[REDACTED]";
                }
                else if (obj[key] is JsonNode child)
                {
                    ScrubNode(child);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not null)
                {
                    ScrubNode(item);
                }
            }
        }
    }
}
