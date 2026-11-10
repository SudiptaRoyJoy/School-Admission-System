namespace Admission.Api.Domain;

public sealed class AdmissionClass
{
    private AdmissionClass() { }

    public AdmissionClass(string id, string name, int totalSeats)
    {
        Id = id;
        Name = name;
        TotalSeats = totalSeats;
    }

    public string Id { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int TotalSeats { get; private set; }
    public DateTime CreatedAt { get; private set; }
}
