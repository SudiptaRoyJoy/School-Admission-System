using Admission.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Admission.Api.Data;

public sealed class AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : DbContext(options)
{
    public DbSet<AdmissionClass> Classes => Set<AdmissionClass>();
    public DbSet<AdmissionHold> Holds => Set<AdmissionHold>();
    public DbSet<ClassSeatSummaryRow> ClassSeatSummaries => Set<ClassSeatSummaryRow>();
    public DbSet<HoldReadRow> HoldReadRows => Set<HoldReadRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var classes = modelBuilder.Entity<AdmissionClass>();
        classes.ToTable("classes");
        classes.HasKey(x => x.Id);
        classes.Property(x => x.Id).HasColumnName("id").HasMaxLength(34).IsRequired();
        classes.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        classes.Property(x => x.TotalSeats).HasColumnName("total_seats").IsRequired();
        classes.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("clock_timestamp()")
            .ValueGeneratedOnAdd();

        var holds = modelBuilder.Entity<AdmissionHold>();
        holds.ToTable("holds");
        holds.HasKey(x => x.Id);
        holds.Property(x => x.Id).HasColumnName("id").HasMaxLength(34).IsRequired();
        holds.Property(x => x.ClassId).HasColumnName("class_id").HasMaxLength(34).IsRequired();
        holds.Property(x => x.StudentId).HasColumnName("student_id").HasMaxLength(17).IsRequired();
        holds.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion(
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<HoldStatus>(value, ignoreCase: true))
            .HasMaxLength(12)
            .IsRequired();
        holds.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
        holds.Property(x => x.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");
        holds.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamp with time zone");
        holds.Property(x => x.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamp with time zone");
        holds.HasOne<AdmissionClass>()
            .WithMany()
            .HasForeignKey(x => x.ClassId)
            .OnDelete(DeleteBehavior.Restrict);
        holds.HasIndex(x => new { x.ClassId, x.StudentId })
            .HasDatabaseName("ux_holds_active_student_class")
            .IsUnique()
            .HasFilter("status IN ('HELD','CONFIRMED')");
        holds.HasIndex(x => new { x.StudentId, x.CreatedAt })
            .HasDatabaseName("ix_holds_student_created");
        holds.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt })
            .HasDatabaseName("ix_holds_class_status_expiry");
        holds.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_holds_due_expiry")
            .HasFilter("status = 'HELD'");

        modelBuilder.Entity<ClassSeatSummaryRow>().HasNoKey();
        modelBuilder.Entity<HoldReadRow>().HasNoKey();
    }
}
