namespace Admission.Api.Features.Classes;

public sealed record CreateClassRequest(string? Name, int? TotalSeats);
public sealed record CreateClassResponse(string Id, string Name, int TotalSeats);
public sealed record ClassSeatResponse(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);
