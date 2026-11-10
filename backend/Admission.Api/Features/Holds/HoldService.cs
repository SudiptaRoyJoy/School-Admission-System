using System.Data;
using System.Data.Common;
using Admission.Api.Common;
using Admission.Api.Data;
using Admission.Api.Domain;
using Admission.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Admission.Api.Features.Holds;

public sealed class HoldService(AdmissionDbContext db, IConfiguration configuration)
{
    private readonly int _holdDurationSeconds = ReadHoldDuration(configuration);

    public async Task<CreateHoldResponse> CreateAsync(string classId, string studentId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = db.Database.GetDbConnection();
        var dbTransaction = db.Database.CurrentTransaction!.GetDbTransaction();

        try
        {
            // The class row is the per-pool mutex. It coordinates all API processes through PostgreSQL.
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = dbTransaction;
                lockCommand.CommandText = "SELECT id FROM classes WHERE id = @id FOR UPDATE";
                AddParameter(lockCommand, "id", classId);
                var lockedId = await lockCommand.ExecuteScalarAsync(cancellationToken);
                if (lockedId is null)
                    throw ApiException.NotFound("CLASS_NOT_FOUND", "The requested class and shift could not be found.");
            }

            // Capture the authoritative DB time only after the row lock is acquired.
            var now = await ReadDatabaseTimeAsync(connection, dbTransaction, cancellationToken);

            // Free this student's expired partial-index entry before checking for a new application.
            await db.Holds
                .Where(h => h.ClassId == classId
                    && h.StudentId == studentId
                    && h.Status == HoldStatus.Held
                    && h.ExpiresAt <= now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(h => h.Status, HoldStatus.Expired), cancellationToken);

            var existingStatus = await db.Holds
                .Where(h => h.ClassId == classId
                    && h.StudentId == studentId
                    && (h.Status == HoldStatus.Confirmed
                        || (h.Status == HoldStatus.Held && h.ExpiresAt > now)))
                .Select(h => (HoldStatus?)h.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingStatus == HoldStatus.Confirmed)
                throw ApiException.Conflict("ALREADY_ADMITTED", "This student already has a confirmed admission for this class and shift.");
            if (existingStatus == HoldStatus.Held)
                throw ApiException.Conflict("ALREADY_APPLIED", "This student already has an active seat hold for this class and shift.");

            var totalSeats = await db.Classes
                .Where(c => c.Id == classId)
                .Select(c => c.TotalSeats)
                .SingleAsync(cancellationToken);

            var occupied = await db.Holds.CountAsync(h => h.ClassId == classId
                && (h.Status == HoldStatus.Confirmed
                    || (h.Status == HoldStatus.Held && h.ExpiresAt > now)), cancellationToken);

            if (occupied >= totalSeats)
                throw ApiException.Conflict("NO_SEATS_AVAILABLE", "No seats are available in this class and shift right now.");

            var hold = new AdmissionHold(
                SecureIdGenerator.NewId("h_"),
                classId,
                studentId,
                now,
                now.AddSeconds(_holdDurationSeconds));

            db.Holds.Add(hold);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new CreateHoldResponse(
                hold.Id,
                hold.ClassId,
                hold.StudentId,
                "HELD",
                DateTime.SpecifyKind(hold.ExpiresAt, DateTimeKind.Utc));
        }
        catch (DbUpdateException ex) when (IsActiveStudentUniqueViolation(ex))
        {
            throw ApiException.Conflict("ALREADY_APPLIED", "This student already has an active application for this class and shift.");
        }
    }

    public async Task<HoldDetailsResponse> GetByIdAsync(string holdId, CancellationToken cancellationToken)
    {
        var hold = await ReadHoldRowAsync(holdId, cancellationToken);
        if (hold is null)
            throw ApiException.NotFound("HOLD_NOT_FOUND", "The requested application could not be found.");

        return new HoldDetailsResponse(hold.HoldId, hold.ClassId, hold.Status, AsUtc(hold.ExpiresAt));
    }

    public async Task<ConfirmHoldResponse> ConfirmAsync(string holdId, string studentId, CancellationToken cancellationToken)
    {
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE holds
            SET status = 'CONFIRMED', confirmed_at = clock_timestamp()
            WHERE id = {holdId}
              AND student_id = {studentId}
              AND status = 'HELD'
              AND expires_at > clock_timestamp()
            """, cancellationToken);

        if (updated == 1)
            return new ConfirmHoldResponse(holdId, "CONFIRMED");

        var hold = await ReadHoldRowAsync(holdId, cancellationToken);
        if (hold is null)
            throw ApiException.NotFound("HOLD_NOT_FOUND", "The requested application could not be found.");
        if (!string.Equals(hold.StudentId, studentId, StringComparison.Ordinal))
            throw ApiException.Forbidden("NOT_HOLD_OWNER", "Only the student who created this application can confirm it.");
        if (hold.Status == "CONFIRMED")
            return new ConfirmHoldResponse(holdId, "CONFIRMED");
        if (hold.Status == "CANCELLED")
            throw ApiException.Conflict("HOLD_CANCELLED", "This application was cancelled and cannot be confirmed.");
        if (hold.Status == "EXPIRED")
            throw ApiException.Conflict("HOLD_EXPIRED", "The seat hold has expired. Please apply again if seats are available.");

        throw ApiException.Conflict("HOLD_EXPIRED", "This seat hold is no longer active. Please refresh and try again.");
    }

    public async Task CancelAsync(string holdId, string studentId, CancellationToken cancellationToken)
    {
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE holds
            SET status = 'CANCELLED', cancelled_at = clock_timestamp()
            WHERE id = {holdId}
              AND student_id = {studentId}
              AND status = 'HELD'
              AND expires_at > clock_timestamp()
            """, cancellationToken);

        if (updated == 1)
            return;

        var hold = await ReadHoldRowAsync(holdId, cancellationToken);
        if (hold is null)
            throw ApiException.NotFound("HOLD_NOT_FOUND", "The requested application could not be found.");
        if (!string.Equals(hold.StudentId, studentId, StringComparison.Ordinal))
            throw ApiException.Forbidden("NOT_HOLD_OWNER", "Only the student who created this application can cancel it.");
        if (hold.Status == "CANCELLED")
            return; // Idempotent cancellation.
        if (hold.Status == "CONFIRMED")
            throw ApiException.Conflict("HOLD_ALREADY_CONFIRMED", "A confirmed admission cannot be cancelled in this assignment.");
        if (hold.Status == "EXPIRED")
            throw ApiException.Conflict("HOLD_EXPIRED", "This seat hold has expired and can no longer be cancelled.");

        throw ApiException.Conflict("HOLD_EXPIRED", "This seat hold is no longer active.");
    }

    public async Task<IReadOnlyList<StudentHoldResponse>> GetForStudentAsync(string studentId, CancellationToken cancellationToken)
    {
        var rows = await db.HoldReadRows
            .FromSqlInterpolated($"""
                SELECT
                    h.id AS hold_id,
                    h.class_id,
                    h.student_id,
                    CASE
                        WHEN h.status = 'HELD' AND h.expires_at <= statement_timestamp() THEN 'EXPIRED'
                        ELSE h.status
                    END AS status,
                    h.expires_at,
                    c.name AS class_name
                FROM holds h
                INNER JOIN classes c ON c.id = h.class_id
                WHERE h.student_id = {studentId}
                ORDER BY h.created_at DESC, h.id DESC
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(row => new StudentHoldResponse(
            row.HoldId,
            row.ClassId,
            row.ClassName ?? string.Empty,
            row.Status,
            AsUtc(row.ExpiresAt))).ToArray();
    }

    private Task<HoldReadRow?> ReadHoldRowAsync(string holdId, CancellationToken cancellationToken)
    {
        return db.HoldReadRows
            .FromSqlInterpolated($"""
                SELECT
                    h.id AS hold_id,
                    h.class_id,
                    h.student_id,
                    CASE
                        WHEN h.status = 'HELD' AND h.expires_at <= statement_timestamp() THEN 'EXPIRED'
                        ELSE h.status
                    END AS status,
                    h.expires_at,
                    NULL::varchar(100) AS class_name
                FROM holds h
                WHERE h.id = {holdId}
                """)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static async Task<DateTime> ReadDatabaseTimeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT clock_timestamp()";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind((DateTime)value!, DateTimeKind.Utc);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static bool IsActiveStudentUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgres
            && postgres.SqlState == PostgresErrorCodes.UniqueViolation
            && postgres.ConstraintName == "ux_holds_active_student_class";
    }

    private static int ReadHoldDuration(IConfiguration configuration)
    {
        var raw = configuration["HOLD_DURATION_SECONDS"];
        if (string.IsNullOrWhiteSpace(raw))
            return 600;
        if (!int.TryParse(raw, out var seconds) || seconds <= 0)
            throw new InvalidOperationException("HOLD_DURATION_SECONDS must be a positive integer.");
        return seconds;
    }
}
