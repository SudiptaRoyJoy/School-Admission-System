namespace Admission.Api.Features.Holds;

public sealed record CreateHoldResponse(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    DateTime ExpiresAt);

public sealed record HoldDetailsResponse(
    string HoldId,
    string ClassId,
    string Status,
    DateTime ExpiresAt);

public sealed record ConfirmHoldResponse(string HoldId, string Status);

public sealed record StudentHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    DateTime ExpiresAt);
