using Admission.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Admission.Api.Data.Migrations;

[DbContext(typeof(AdmissionDbContext))]
[Migration("202611090001_InitialSchema")]
public sealed class InitialSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE classes (
                id varchar(34) PRIMARY KEY,
                name varchar(100) NOT NULL CHECK (char_length(btrim(name)) BETWEEN 1 AND 100),
                total_seats integer NOT NULL CHECK (total_seats > 0),
                created_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );

            CREATE TABLE holds (
                id varchar(34) PRIMARY KEY,
                class_id varchar(34) NOT NULL REFERENCES classes(id) ON DELETE RESTRICT,
                student_id varchar(17) NOT NULL CHECK (student_id ~ '^[0-9]{17}$'),
                status varchar(12) NOT NULL CHECK (status IN ('HELD','CONFIRMED','EXPIRED','CANCELLED')),
                created_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                confirmed_at timestamptz NULL,
                cancelled_at timestamptz NULL
            );

            CREATE UNIQUE INDEX ux_holds_active_student_class
                ON holds (class_id, student_id)
                WHERE status IN ('HELD','CONFIRMED');

            CREATE INDEX ix_holds_student_created
                ON holds (student_id, created_at DESC);

            CREATE INDEX ix_holds_class_status_expiry
                ON holds (class_id, status, expires_at);

            CREATE INDEX ix_holds_due_expiry
                ON holds (expires_at)
                WHERE status = 'HELD';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS holds; DROP TABLE IF EXISTS classes;");
    }
}
