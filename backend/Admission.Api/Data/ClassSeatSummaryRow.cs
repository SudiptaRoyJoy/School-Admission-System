using System.ComponentModel.DataAnnotations.Schema;

namespace Admission.Api.Data;

public sealed class ClassSeatSummaryRow
{
    [Column("id")] public string Id { get; set; } = string.Empty;
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("total_seats")] public int TotalSeats { get; set; }
    [Column("held_seats")] public int HeldSeats { get; set; }
    [Column("confirmed_seats")] public int ConfirmedSeats { get; set; }
    [Column("available_seats")] public int AvailableSeats { get; set; }
}
