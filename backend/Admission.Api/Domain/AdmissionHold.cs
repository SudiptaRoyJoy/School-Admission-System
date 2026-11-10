namespace Admission.Api.Domain;

public sealed class AdmissionHold
{
    private AdmissionHold() { }

    public AdmissionHold(
        string id,
        string classId,
        string studentId,
        DateTime createdAt,
        DateTime expiresAt)
    {
        Id = id;
        ClassId = classId;
        StudentId = studentId;
        Status = HoldStatus.Held;
        CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc);
        ExpiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc);
    }

    public string Id { get; private set; } = null!;
    public string ClassId { get; private set; } = null!;
    public string StudentId { get; private set; } = null!;
    public HoldStatus Status { get; set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
