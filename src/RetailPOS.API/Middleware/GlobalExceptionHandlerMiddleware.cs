using System.Net;
using System.Text.Json;
using RetailPOS.API.Models;

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
            await HandleExceptionAsync(context, ex);
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
