namespace AdminPanel.Infrastructure;

public class ApiException : Exception
{
    public string? Code { get; }
    public int StatusCode { get; }

    public ApiException(string message, int statusCode = 400, string? code = null) 
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public ApiException(string message, Exception innerException, int statusCode = 500) 
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
