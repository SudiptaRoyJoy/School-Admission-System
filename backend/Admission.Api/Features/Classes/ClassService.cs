using Admission.Api.Common;
using Admission.Api.Data;
using Admission.Api.Domain;
using Admission.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Admission.Api.Features.Classes;

public sealed class ClassService(AdmissionDbContext db)
{
    private const string ListSql = """
        WITH db_time AS (SELECT statement_timestamp() AS ts)
        SELECT
            c.id,
            c.name,
            c.total_seats,
            (COUNT(h.id) FILTER (
                WHERE h.status = 'HELD' AND h.expires_at > db_time.ts
            ))::integer AS held_seats,
            (COUNT(h.id) FILTER (
                WHERE h.status = 'CONFIRMED'
            ))::integer AS confirmed_seats,
            (
                c.total_seats
                - (COUNT(h.id) FILTER (WHERE h.status = 'HELD' AND h.expires_at > db_time.ts))
                - (COUNT(h.id) FILTER (WHERE h.status = 'CONFIRMED'))
            )::integer AS available_seats
        FROM classes c
        CROSS JOIN db_time
        LEFT JOIN holds h ON h.class_id = c.id
            AND (h.status = 'CONFIRMED' OR (h.status = 'HELD' AND h.expires_at > db_time.ts))
        GROUP BY c.id, c.name, c.total_seats, c.created_at, db_time.ts
        ORDER BY c.created_at, c.id
        """;

    public async Task<CreateClassResponse> CreateAsync(CreateClassRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw ApiException.BadRequest("INVALID_REQUEST", "Enter a class and shift name.");
        if (name.Length > 100)
            throw ApiException.BadRequest("INVALID_REQUEST", "The class and shift name must be 100 characters or fewer.");
        if (request.TotalSeats is null || request.TotalSeats <= 0)
            throw ApiException.BadRequest("INVALID_REQUEST", "Total seats must be a positive whole number.");

        var entity = new AdmissionClass(SecureIdGenerator.NewId("c_"), name, request.TotalSeats.Value);
        db.Classes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return new CreateClassResponse(entity.Id, entity.Name, entity.TotalSeats);
    }

    public async Task<IReadOnlyList<ClassSeatResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ClassSeatSummaries
            .FromSqlRaw(ListSql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToArray();
    }

    public async Task<ClassSeatResponse> GetByIdAsync(string classId, CancellationToken cancellationToken)
    {
        var sql = """
            WITH db_time AS (SELECT statement_timestamp() AS ts)
            SELECT
                c.id,
                c.name,
                c.total_seats,
                (COUNT(h.id) FILTER (
                    WHERE h.status = 'HELD' AND h.expires_at > db_time.ts
                ))::integer AS held_seats,
                (COUNT(h.id) FILTER (
                    WHERE h.status = 'CONFIRMED'
                ))::integer AS confirmed_seats,
                (
                    c.total_seats
                    - (COUNT(h.id) FILTER (WHERE h.status = 'HELD' AND h.expires_at > db_time.ts))
                    - (COUNT(h.id) FILTER (WHERE h.status = 'CONFIRMED'))
                )::integer AS available_seats
            FROM classes c
            CROSS JOIN db_time
            LEFT JOIN holds h ON h.class_id = c.id
            AND (h.status = 'CONFIRMED' OR (h.status = 'HELD' AND h.expires_at > db_time.ts))
            WHERE c.id = {0}
            GROUP BY c.id, c.name, c.total_seats, c.created_at, db_time.ts
            """;

        var row = await db.ClassSeatSummaries
            .FromSqlRaw(sql, classId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (row.Count == 0)
            throw ApiException.NotFound("CLASS_NOT_FOUND", "The requested class and shift could not be found.");

        return Map(row[0]);
    }

    private static ClassSeatResponse Map(ClassSeatSummaryRow row)
    {
        if (row.AvailableSeats < 0)
            throw new InvalidOperationException($"Capacity invariant violated for class {row.Id}: available seats are negative.");

        return new ClassSeatResponse(
            row.Id,
            row.Name,
            row.TotalSeats,
            row.HeldSeats,
            row.ConfirmedSeats,
            row.AvailableSeats);
    }
}
