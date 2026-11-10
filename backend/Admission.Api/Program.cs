using Admission.Api.Common;
using Admission.Api.Data;
using Admission.Api.Features.Classes;
using Admission.Api.Features.Holds;
using Admission.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
var frontendOrigin = builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:3000";

builder.Services.AddDbContext<AdmissionDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<HoldExpiryWorker>();

builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
    policy.WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("X-Server-Utc")));

var app = builder.Build();
// CORS wraps the error handler so even 4xx/5xx JSON responses retain CORS headers.
app.UseCors("frontend");
app.UseApiErrorHandling();

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    await db.Database.MigrateAsync();
    return;
}

app.MapGet("/health", async (AdmissionDbContext db, CancellationToken cancellationToken) =>
{
    var connected = await db.Database.CanConnectAsync(cancellationToken);
    if (!connected)
        return Results.Json(new ApiErrorEnvelope(new ApiErrorBody(
            "SERVICE_UNAVAILABLE",
            "The admission service is temporarily unavailable.",
            Array.Empty<ApiErrorDetail>())), statusCode: StatusCodes.Status503ServiceUnavailable);
    return Results.Ok(new { status = "ok" });
});

app.MapPost("/classes", async (
    CreateClassRequest request,
    ClassService service,
    CancellationToken cancellationToken) =>
{
    var created = await service.CreateAsync(request, cancellationToken);
    return Results.Created($"/classes/{created.Id}", created);
});

app.MapGet("/classes", async (ClassService service, CancellationToken cancellationToken) =>
    Results.Ok(await service.GetAllAsync(cancellationToken)));

app.MapGet("/classes/{classId}", async (
    string classId,
    ClassService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.GetByIdAsync(classId, cancellationToken)));

app.MapPost("/classes/{classId}/holds", async (
    string classId,
    HttpRequest request,
    HoldService service,
    CancellationToken cancellationToken) =>
{
    var studentId = StudentIdValidator.FromRequest(request);
    var created = await service.CreateAsync(classId, studentId, cancellationToken);
    return Results.Created($"/holds/{created.HoldId}", created);
});

app.MapGet("/holds/{holdId}", async (
    string holdId,
    HoldService service,
    CancellationToken cancellationToken) =>
    Results.Ok(await service.GetByIdAsync(holdId, cancellationToken)));

app.MapPost("/holds/{holdId}/confirm", async (
    string holdId,
    HttpRequest request,
    HoldService service,
    CancellationToken cancellationToken) =>
{
    var studentId = StudentIdValidator.FromRequest(request);
    return Results.Ok(await service.ConfirmAsync(holdId, studentId, cancellationToken));
});

app.MapDelete("/holds/{holdId}", async (
    string holdId,
    HttpRequest request,
    HoldService service,
    CancellationToken cancellationToken) =>
{
    var studentId = StudentIdValidator.FromRequest(request);
    await service.CancelAsync(holdId, studentId, cancellationToken);
    return Results.NoContent();
});

app.MapGet("/me/holds", async (
    HttpRequest request,
    HoldService service,
    CancellationToken cancellationToken) =>
{
    var studentId = StudentIdValidator.FromRequest(request);
    return Results.Ok(await service.GetForStudentAsync(studentId, cancellationToken));
});

app.MapFallback(async (HttpContext context) =>
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsJsonAsync(new ApiErrorEnvelope(new ApiErrorBody(
        "ROUTE_NOT_FOUND",
        "The requested API route does not exist.",
        Array.Empty<ApiErrorDetail>())), cancellationToken: context.RequestAborted);
});

app.Run();

public partial class Program { }
