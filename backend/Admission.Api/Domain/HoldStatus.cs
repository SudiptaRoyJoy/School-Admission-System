namespace Admission.Api.Domain;

public enum HoldStatus
{
    Held,
    Confirmed,
    Expired,
    Cancelled
}

public static class HoldStatusExtensions
{
    public static string ToApiValue(this HoldStatus status) => status switch
    {
        HoldStatus.Held => "HELD",
        HoldStatus.Confirmed => "CONFIRMED",
        HoldStatus.Expired => "EXPIRED",
        HoldStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };
}
