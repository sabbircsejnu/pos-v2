using System.Net;
using System.Text.Json;
using RetailPOS.API.Exceptions;
using RetailPOS.API.Models;
using RetailPOS.Application.Authorization;
using RetailPOS.Application.HR.Employees;
using RetailPOS.Application.Customers;
using RetailPOS.Application.Sales.Checkout;
using RetailPOS.Application.Variations;
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

            case VariationInUseException inUse:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = inUse.Message;
                errorResponse.Errors = new List<string> { inUse.Message };
                errorResponse.Data = inUse.Products;
                break;

            case HrEmployeeConcurrencyException:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            case KeyNotFoundException:
                response.StatusCode = (int)HttpStatusCode.NotFound;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            case BusinessContextRequiredException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                errorResponse.ErrorCode = BusinessContextRequiredException.ErrorCode;
                break;

            case CustomerPhoneConflictException phoneConflict:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = phoneConflict.Message;
                errorResponse.Errors = new List<string> { phoneConflict.Message };
                errorResponse.ErrorCode = "CUSTOMER_PHONE_EXISTS";
                errorResponse.Data = new
                {
                    id = phoneConflict.Existing.Id,
                    name = phoneConflict.Existing.Name,
                    phone = phoneConflict.Existing.Phone
                };
                break;

            case RetailPOS.Application.Uom.UomConflictException uomConflict:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = uomConflict.Message;
                errorResponse.Errors = new List<string> { uomConflict.Message };
                errorResponse.ErrorCode = uomConflict.Code;
                errorResponse.Data = uomConflict.Details;
                break;

            case RetailPOS.Application.Uom.UomValidationException uomValidation:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = uomValidation.Message;
                errorResponse.Errors = new List<string> { uomValidation.Message };
                errorResponse.ErrorCode = uomValidation.Code;
                break;

            case SaleCheckoutConflictException checkoutConflict:
                response.StatusCode = (int)HttpStatusCode.Conflict;
                errorResponse.Success = false;
                errorResponse.Message = checkoutConflict.Message;
                errorResponse.Errors = new List<string> { checkoutConflict.Message };
                errorResponse.ErrorCode = checkoutConflict.Code;
                errorResponse.Data = checkoutConflict.Details;
                break;

            case SaleCheckoutValidationException checkoutValidation:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = checkoutValidation.Message;
                errorResponse.Errors = new List<string> { checkoutValidation.Message };
                errorResponse.ErrorCode = checkoutValidation.Code;
                break;

            case SaleCheckoutForbiddenException checkoutForbidden:
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                errorResponse.Success = false;
                errorResponse.Message = checkoutForbidden.Message;
                errorResponse.Errors = new List<string> { checkoutForbidden.Message };
                errorResponse.ErrorCode = checkoutForbidden.Code;
                break;

            case RetailPOS.Application.Pos.CashSessionException cashSession:
                response.StatusCode = cashSession.HttpStatus;
                errorResponse.Success = false;
                errorResponse.Message = cashSession.Message;
                errorResponse.Errors = new List<string> { cashSession.Message };
                errorResponse.ErrorCode = cashSession.Code;
                break;

            case InvalidOperationException:
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                errorResponse.Success = false;
                errorResponse.Message = exception.Message;
                errorResponse.Errors = new List<string> { exception.Message };
                break;

            // An authenticated caller refused by a permission, scope or entitlement check is a 403.
            // 401 is reserved for missing/invalid credentials (JWT challenge and the auth endpoints),
            // because clients treat 401 as "the session is gone".
            case UnauthorizedAccessException:
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                errorResponse.Success = false;
                errorResponse.Message = "You do not have permission to perform this action.";
                errorResponse.Errors = new List<string> { exception.Message };
                errorResponse.ErrorCode = "forbidden";
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
