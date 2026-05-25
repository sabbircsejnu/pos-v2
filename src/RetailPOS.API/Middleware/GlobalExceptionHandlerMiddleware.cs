using System.Net;
using System.Text.Json;
using RetailPOS.API.Exceptions;
using RetailPOS.API.Models;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;

namespace RetailPOS.API.Middleware;

/// <summary>
/// Global exception handler middleware that catches all unhandled exceptions
/// and returns a standardized error response
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await TryRecordFailureAuditAsync(context, ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task TryRecordFailureAuditAsync(HttpContext context, Exception ex)
    {
        try
        {
            var method = context.Request.Method;
            if (method != HttpMethods.Post && method != HttpMethods.Put
                && method != HttpMethods.Patch && method != HttpMethods.Delete)
                return;

            var auditService = context.RequestServices.GetService(typeof(IAuditService)) as IAuditService;
            if (auditService is null) return;

            var path = context.Request.Path.Value ?? "";
            await auditService.RecordFailureAsync(
                actionType: AuditActionType.Update,
                module: AuditModule.System,
                summary: $"{method} {path} failed",
                entityType: null,
                entityId: null,
                errorMessage: ex.Message);
        }
        catch (Exception auditEx)
        {
            _logger.LogWarning(auditEx, "Failed to record failure audit event");
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        var response = context.Response;
        response.ContentType = "application/json";

        var errorResponse = new ApiResponse();

        switch (exception)
        {
            case ResourceInUseException inUse:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = inUse.Message;
                errorResponse.Errors = new List<string> { inUse.Message };
                errorResponse.Data = inUse.References;
                break;

            case KeyNotFoundException:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            case InvalidOperationException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            case UnauthorizedAccessException:
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                errorResponse.Success = false;
                errorResponse.Message = "Unauthorized access";
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            case ArgumentException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            default:
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                errorResponse.Success = false;
                errorResponse.Message = "An internal server error occurred";
                errorResponse.Errors = new List<string> { exception.Message };
                break;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var result = JsonSerializer.Serialize(errorResponse, jsonOptions);
        await response.WriteAsync(result);
    }
}
