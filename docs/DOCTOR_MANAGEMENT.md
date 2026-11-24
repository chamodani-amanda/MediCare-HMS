# Doctor Management

Implemented inside the existing MediCare MVC application, preserving Patient Management and the current feature/patient-list branch. No separate Doctor Management requirements document was present; the role and schedule rules below are the explicit implementation assumptions.

## Behavior and permissions

| Capability | Administrator | Doctor | Receptionist | Other / signed out |
| --- | --- | --- | --- | --- |
| Active doctor directory, profiles, weekly availability | Yes | Yes | Yes | Denied |
| Include inactive doctors and view inactive profiles | Yes | No | No | Denied |
| Register/edit doctors, change status or linked account | Yes | No | No | Denied |
| Manage departments and working schedules | Yes | No | No | Denied |

The directory searches name, normalized medical registration number, specialization and department with pagination (20 per page). It treats user wildcard characters literally. Profiles show qualifications, specialization, department, email, phone, active/inactive status and published weekly working hours.

Registration requires names, a medical registration number, qualifications, specialization and a valid department. Optional contact fields have server-side format and length validation. Registration numbers, email addresses and department names use invariant uppercase keys for duplicate checks; corresponding PostgreSQL indexes enforce uniqueness. Blank optional values become null.

Doctors can be linked to an existing, available Identity account with the Doctor role. One account can link to at most one doctor. Staff Accounts remains responsible for creating accounts, credentials, roles and sign-in lockout. Doctor forms do not accept or store passwords, create Identity users or assign roles. A locked current link stays visible rather than silently clearing when editing; an administrator must explicitly unlink it or select an eligible account before saving. Deactivating a doctor affects clinical availability, not the linked account's ability to sign in.

Departments can be renamed, described, activated and deactivated. Active doctors must be moved or deactivated before their department can be deactivated. Doctors, departments and schedules have no deletion endpoint.

## Schedules and concurrency

Schedules recur weekly in Sri Lanka local time (Asia/Colombo). A valid day and start/end times are required. End must follow start on the same day; overnight entries are rejected. Adjacent shifts are allowed. Active shifts on the same day must not overlap, including contained and enclosing intervals. Inactive schedules remain stored and can be edited/reactivated, with overlap validation on reactivation. Inactive doctors cannot gain active schedules. Profiles clearly mark inactive doctors as unavailable even though their schedule history remains.

Doctor, department and schedule updates use Guid optimistic concurrency tokens. Schedule forms include both schedule and doctor versions. Every schedule change also rotates the doctor's token; concurrent additions therefore contend on the same doctor row. In PostgreSQL, SaveChanges commits the token update, schedule change and audit event in one transaction. A competing writer loses with a concurrency error rather than committing an overlapping shift. This requires future application writers of doctor schedules to follow the same protocol; the filtered unique index prevents identical start-time duplicates but is not a PostgreSQL range exclusion constraint.

Doctor creation/department assignment also rotates the destination department's concurrency token, coordinating with department deactivation. Department deactivation and doctor/schedule edits reject stale versions. The user must reload a stale form before saving again.

Every mutation uses POST and antiforgery validation. Sensitive changes stage audit events in the existing security audit table and save them with the entity change. Events include DoctorCreated, DoctorUpdated, DoctorAccountLinkChanged, DoctorActivated/Deactivated, DepartmentCreated/Updated and DoctorScheduleCreated/Updated. Audit targets are entity IDs in the existing TargetUserId column; passwords and clinical/contact details are not logged.

## Database and backup

Read-only verification confirmed the actual database is medicare_dev, PostgreSQL 18.3, with the four existing migrations through AddPatientDocumentsAndConcurrency. The public schema contains the seven Identity tables, patients, patient_documents, security_audit_logs and migration history. Patient search/profile/edit/document queries still passed against PostgreSQL.

Created migration: **20261118184537_AddDoctorManagement**. Reviewed Up operations create only departments, doctors and doctor_schedules plus indexes, primary keys, restrictive foreign keys and schedule checks. It does not alter existing patient, audit or Identity tables. The Identity relationship references AspNetUsers without changing that table. Forward SQL: tools/DoctorManagement-migration.sql. Down drops the new tables and their data; do not run rollback casually.

**Migration 20261118184537_AddDoctorManagement was applied to medicare_dev after explicit user approval.** Before application, the actual connected database was reconfirmed, the existing backup's size/checksum and both archive checks were repeated, and generated SQL was matched against the reviewed additive script. No unexpected operations or other pending migrations were found. All three new tables, expected columns, primary keys, indexes, restrictive foreign keys, schedule checks and concurrency columns passed post-application verification. No migrations remain pending.

Full-row SHA-256 fingerprints and counts were compared before and after: the existing patient (1 row), patient document (1), security audit records (9) and all seven Identity tables were preserved. Identity counts were 3 users, 7 roles, 3 user-role links and zero rows in the four other Identity tables. Provider errors, connection strings and row contents were not printed.

A fresh custom-format backup was created outside Git without overwriting the earlier Step 7 backup:

- Path: D:\MediCare-Backups\medicare_dev_before_step7_20261118_184105_5c6cb84e.backup
- Size: 24,415 bytes.
- SHA-256: FDB64611627EF54E664A671C6B5B16314E2F74A5A6BC9A7B9037B3BBE0935900.
- pg_restore --list: passed; archive identifies medicare_dev and contains table data.
- Full offline pg_restore read to Windows NUL: passed, without connecting a restore process to any database.

The existing backup helper retains its before_step7 filename prefix; this timestamped file is the verified pre-Doctor Management backup. Its size and checksum were unchanged and pg_restore --list plus a full offline read passed again immediately before application. No actual database restore rehearsal was performed. Never access hms_dev or reset/recreate data.

## Changed files

New:

- Models/Department.cs, Doctor.cs, DoctorSchedule.cs.
- ViewModels/DoctorManagementViewModels.cs.
- Controllers/ClinicalAdministrationControllerBase.cs, DepartmentsController.cs, DoctorsController.cs, DoctorSchedulesController.cs.
- Views/Doctors/Index.cshtml, Form.cshtml, Details.cshtml.
- Views/Departments/Index.cshtml, Form.cshtml.
- Views/DoctorSchedules/Index.cshtml, Form.cshtml.
- Data/Migrations/20261118184537_AddDoctorManagement.cs and its Designer.cs companion.
- tests/MediCare.Web.Tests/DoctorManagementTests.cs and DoctorPersistenceTests.cs.
- tools/DoctorManagement-migration.sql and docs/DOCTOR_MANAGEMENT.md.
- tools/MediCare.DoctorDatabaseCheck/MediCare.DoctorDatabaseCheck.csproj and Program.cs: migration preflight/schema/preservation checks and rollback-only PostgreSQL integration checks.

Modified:

- Data/MediCareDbContext.cs: DbSets, mappings, concurrency, indexes and constraints.
- Data/Migrations/MediCareDbContextModelSnapshot.cs: generated snapshot.
- Views/Shared/_Layout.cshtml: role-aware Doctors and Departments links, retaining existing patient navigation.
- Views/SecurityAuditLogs/Index.cshtml: include new administrative event types in explanatory text.
- tools/MediCare.DatabaseCheck/Program.cs: report public table/column counts without secrets or row contents.

Paths above are relative to src/MediCare.Web except tests, tools and docs. All earlier uncommitted Patient Management changes were preserved.

## Verification and remaining checks

Build/tests use Release configuration because an existing user-started MediCare.Web Debug process locks its executable and DLL. That process was preserved. Commands: `dotnet build --configuration Release` and `dotnet test --configuration Release --no-build --no-restore`. A normal Debug build should work after the running application is stopped. Restart the local application to load the new module against the now-migrated schema.

Tests use isolated EF InMemory databases and a TestHost with test-only header authentication; they never mutate medicare_dev. They exercise real MVC routes and compiled Razor views, role restrictions, antiforgery, duplicate registration/contact/account links, locked links, department validation/deactivation protection, directory search/pagination, active/inactive visibility, schedule overlaps/ranges, schedule deactivation and stale updates. Persistence checks inspect the actual PostgreSQL migration and generated overlap SQL, and exercise concurrency failures for all three entity types.

Post-migration Release build passed with zero warnings and zero errors. All 65 automated tests passed with zero failures/skips: 31 existing Patient Management cases plus 34 Doctor Management cases (30 HTTP cases and 4 persistence/concurrency cases). EF reports no model changes since AddDoctorManagement and no migrations remain pending. Git whitespace checks passed and the branch remains feature/patient-list. The test runner emitted its existing informational temporary-artifact-directory fallback message; it did not affect results.

Post-application PostgreSQL checks passed using the real controllers and EF provider inside an outer transaction that was always rolled back. They verified department/doctor creation, duplicate registrations, unique Doctor-role account links, overlap rejection and adjacent shifts, stale doctor writes, stale aggregate versions for schedule writers, and atomic rollback of failed schedule/audit writes. Savepoint-isolated probes verified unique department/registration/account/active-shift indexes, invalid department/doctor foreign keys, and invalid schedule day/time constraints. No test departments, doctors, schedules, Identity users/user-role links or audit events were retained; all table fingerprints matched after rollback.

Run `dotnet run --project tools/MediCare.DoctorDatabaseCheck --configuration Release -- --integration` from the repository root to repeat rollback-only integration checks. With no flags, the helper only verifies schema and existing data. Its --apply-approved path is a migration application command and must only be used with explicit approval; it also refuses an unexpected pending-migration set.

Remaining browser checks: restart the app, sign in with real Identity accounts over HTTPS, set up a department, register/link/edit a doctor, exercise directory search/pagination, publish weekly schedules and reject overlaps, change active/inactive status, and verify two-tab stale-update messages. Check visible audit records for those real actions. The PostgreSQL concurrency probes use stale EF contexts sharing one rollback-only transaction; genuinely simultaneous independent-session stress/load testing and an actual backup restore rehearsal were not performed.

Availability describes recurring working hours, not appointment booking, holiday exceptions, leave or date-specific rosters. Those are outside this module. No commit, push, merge or branch switch is performed.
