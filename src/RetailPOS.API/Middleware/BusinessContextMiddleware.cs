using System.Security.Claims;
using MediatR;
using RetailPOS.Application.Authentication.Queries;

namespace RetailPOS.API.Middleware;

public sealed class BusinessContextMiddleware(RequestDelegate next, ILogger<BusinessContextMiddleware> logger)
{
    public const string HeaderName = "X-Business-Id";
    public const string ItemName = "ValidatedSuperAdminBusinessId";

    public async Task InvokeAsync(HttpContext context, ISender sender)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            await next(context);
            return;
        }
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        if (header.Count != 1 || !long.TryParse(header.ToString(), out var businessId) || businessId <= 0)
        {
            await Results.BadRequest(new { message = "A positive Business identifier is required." })
                .ExecuteAsync(context);
            return;
        }
        if (!long.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        try
        {
            var result = await sender.Send(new ValidateBusinessContextQuery(userId, businessId),
                context.RequestAborted);
            context.Items[ItemName] = result.BusinessId;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.LogWarning("Business context rejected for user {UserId}: {Reason}", userId, exception.Message);
            await Results.Json(new { message = "The requested Business context is not authorized." },
                statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
