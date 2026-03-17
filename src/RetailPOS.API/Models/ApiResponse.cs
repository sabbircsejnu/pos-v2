namespace RetailPOS.API.Models;

/// <summary>
/// Standardized API response wrapper for all endpoints
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }

    public static ApiResponse<T> SuccessResponse(T data, string message = "Operation successful")
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data
        };
    }

    public static ApiResponse<T> SuccessResponse(string message)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message
        };
    }

    public static ApiResponse<T> ErrorResponse(string error)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = error,
            Errors = new List<string> { error }
        };
    }

    public static ApiResponse<T> ErrorResponse(List<string> errors)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = "One or more errors occurred",
            Errors = errors
        };
    }
}

/// <summary>
/// Non-generic version for responses without data
/// </summary>
public class ApiResponse : ApiResponse<object>
{
}
