using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Web.Data.Migrations
{
    public partial class EnforceStaffConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Prevent multiple unfinished attendance
            // records for the same employee.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "UX_attendances_one_open_per_employee"
                ON public.attendances ("EmployeeId")
                WHERE "CheckOutAtUtc" IS NULL;
                """);

            // Enable GiST equality support for UUID values.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS btree_gist;
                """);

            // Prevent overlapping Pending or Approved leave
            // requests for the same employee.
            migrationBuilder.Sql("""
                ALTER TABLE public.leave_requests
                ADD CONSTRAINT "EX_leave_requests_no_active_overlap"
                EXCLUDE USING gist (
                    "EmployeeId" WITH =,
                    daterange("StartDate", "EndDate", '[]') WITH &&
                )
                WHERE ("Status" IN ('Pending', 'Approved'));
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the leave overlap constraint.
            migrationBuilder.Sql("""
                ALTER TABLE public.leave_requests
                DROP CONSTRAINT IF EXISTS
                "EX_leave_requests_no_active_overlap";
                """);

            // Remove the attendance unique index.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS
                public."UX_attendances_one_open_per_employee";
                """);

            // Leave btree_gist installed because other
            // database objects may depend on it.
        }
    }
}