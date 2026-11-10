namespace Admission.Api.Common;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string code, string message, IReadOnlyList<ApiErrorDetail>? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details ?? Array.Empty<ApiErrorDetail>();
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IReadOnlyList<ApiErrorDetail> Details { get; }

    public static ApiException BadRequest(string code, string message) => new(400, code, message);
    public static ApiException NotFound(string code, string message) => new(404, code, message);
    public static ApiException Conflict(string code, string message) => new(409, code, message);
    public static ApiException Forbidden(string code, string message) => new(403, code, message);
}

public sealed record ApiErrorDetail(string Field, string Message);
public sealed record ApiErrorBody(string Code, string Message, IReadOnlyList<ApiErrorDetail> Details, string? TraceId = null);
public sealed record ApiErrorEnvelope(ApiErrorBody Error);
