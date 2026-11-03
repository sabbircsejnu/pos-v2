namespace RetailPOS.Application.Authorization;

/// <summary>
/// Raised when an authenticated caller invokes a Business-scoped operation without a valid
/// Business context (for example a Super Admin who has not selected a Business). It is a
/// request problem, not an authentication or permission failure, so it must never invalidate
/// the session.
/// </summary>
public sealed class BusinessContextRequiredException : InvalidOperationException
{
    public const string ErrorCode = "business_context_required";
    public const string DefaultMessage = "Business scope is required for this operation.";

    public BusinessContextRequiredException()
        : base(DefaultMessage)
    {
    }

    public BusinessContextRequiredException(string message)
        : base(message)
    {
    }
}
