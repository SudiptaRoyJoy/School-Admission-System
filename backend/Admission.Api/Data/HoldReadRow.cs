using System.ComponentModel.DataAnnotations.Schema;

namespace Admission.Api.Data;

/// <summary>
/// Read-only projection used to calculate a hold's effective status and DB time
/// in one PostgreSQL statement. It is intentionally keyless and never persisted.
/// </summary>
public sealed class HoldReadRow
{
    [Column("hold_id")] public string HoldId { get; set; } = string.Empty;
    [Column("class_id")] public string ClassId { get; set; } = string.Empty;
    [Column("student_id")] public string StudentId { get; set; } = string.Empty;
    [Column("status")] public string Status { get; set; } = string.Empty;
    [Column("expires_at")] public DateTime ExpiresAt { get; set; }
    [Column("class_name")] public string? ClassName { get; set; }
}
