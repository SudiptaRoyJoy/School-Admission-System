using System.Text.Json;
using Npgsql;

namespace Admission.Api.Common;

public static class ApiErrorMiddleware
{
    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Server-Utc"] = DateTimeOffset.UtcNow.ToString("O");
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Task.CompletedTask;
            });

            try
            {
                await next();
            }
            catch (ApiException ex) when (!context.Response.HasStarted)
            {
                await WriteAsync(context, ex.StatusCode,
                    new ApiErrorBody(ex.Code, ex.Message, ex.Details));
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            {
                await WriteAsync(context, 400,
                    new ApiErrorBody("INVALID_REQUEST", "The request body or parameters are invalid.", Array.Empty<ApiErrorDetail>()));
            }
            catch (JsonException) when (!context.Response.HasStarted)
            {
                await WriteAsync(context, 400,
                    new ApiErrorBody("INVALID_REQUEST", "The request body must contain valid JSON.", Array.Empty<ApiErrorDetail>()));
            }
            catch (NpgsqlException ex) when (!context.Response.HasStarted)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiErrorMiddleware")
                    .LogError(ex, "Database unavailable. TraceId={TraceId}", context.TraceIdentifier);
                await WriteAsync(context, 503,
                    new ApiErrorBody("SERVICE_UNAVAILABLE", "The admission service is temporarily unavailable. Please try again.", Array.Empty<ApiErrorDetail>(), context.TraceIdentifier));
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ApiErrorMiddleware")
                    .LogError(ex, "Unhandled request failure. TraceId={TraceId}", context.TraceIdentifier);
                await WriteAsync(context, 500,
                    new ApiErrorBody("INTERNAL_ERROR", "Something went wrong. Please try again. Reference: " + context.TraceIdentifier,
                        Array.Empty<ApiErrorDetail>(), context.TraceIdentifier));
            }
        });
    }

    private static async Task WriteAsync(HttpContext context, int statusCode, ApiErrorBody body)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(body), cancellationToken: context.RequestAborted);
    }
}
