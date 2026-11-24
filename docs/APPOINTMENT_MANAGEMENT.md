# Step 9: Appointment Management

Implemented in the existing MediCare MVC application without replacing Patient or Doctor Management. The branch remains feature/patient-list; earlier uncommitted work is preserved. No commit, push, merge or branch switch was performed.

## Supplied development specification

These rules were supplied by the user during Step 9 and take precedence over the initially proposed defaults. No separate original hospital appointment specification was present in the repository. They can be revised later if the hospital specification differs.

- Statuses: Scheduled, Completed, Cancelled, NoShow.
- Durations: 15, 30, 45 or 60 minutes; start times fall on 15-minute intervals, with no seconds/fractions.
- Only future appointments may be booked/rescheduled, using non-archived patients and active doctors in active departments.
- The entire duration must fit the doctor's recurring weekly working hours. Adjacent working shifts are continuous availability; gaps are not bridged. Existing schedules are same-day, so overnight appointments are rejected.
- Non-cancelled appointments for the same doctor may not overlap, regardless of duration. Adjacent appointments are allowed.
- Only Scheduled appointments can be rescheduled or cancelled. Rescheduling keeps the same patient, but can change doctor/date/time/duration after full validation.
- Completed and NoShow can only be set once the appointment has ended. Completed, Cancelled and NoShow are terminal; they cannot be reopened.
- Cancellation retains the appointment and original reserved interval in history while releasing the slot. An optional administrative cancellation reason is supported; no clinical or payment workflow is introduced.
- Timestamps are UTC in PostgreSQL. Forms, filters, availability, details and the weekly agenda use Asia/Colombo.

Deactivating a doctor or archiving a patient does not automatically cancel existing appointments; staff can still review/cancel their history. New bookings and rescheduling must pass the eligibility rules above.

## Permissions and privacy

| Role | View appointments | Manage bookings/statuses/availability |
| --- | --- | --- |
| Administrator | All | Yes |
| Receptionist | All | Yes |
| Doctor | Only appointments for the doctor linked to the signed-in Identity user | No |
| Other roles / signed out | Denied | Denied |

Doctor scoping is applied to the database query before listing, search, doctor/date/status filtering, details or calendar projection. Spoofed doctor IDs do not expand access. Unlinked Doctor accounts see no appointments; unauthorized details return 404. Doctor dropdowns are scoped to the same account. Appointment views show patient name/number only, without patient phone, email, address, DOB, emergency contacts or medical records. Optional cancellation reasons are visible only to booking managers. Appointment responses disable caching.

All mutations require POST, antiforgery validation, authorized roles and server-side validation. Successful booking, rescheduling, cancellation and status changes save their audit event with the appointment in the same EF SaveChanges transaction. Audit events contain actor/target IDs and action names rather than patient details or appointment notes.

## Booking and concurrency

Create forms receive a random BookingToken. The database has a unique index on this token, preventing double-submit duplicates even when requests race. Repeating an already-saved token with the same patient/doctor/time/duration returns the existing appointment; changing the payload requires a new form. There is no deletion endpoint.

The appointment has a Guid concurrency token. Rescheduling and status forms post the original version; stale submissions are rejected. Booking/rescheduling also rotate patient and doctor tokens, coordinating with patient archival, doctor deactivation and schedule editing. Schedule changes reject any proposal that would strand a future/in-progress Scheduled appointment outside working hours. Their existing shared doctor token coordinates schedule/booking races.

The migration installs the available PostgreSQL btree_gist extension and creates EX_appointments_doctor_time:

    EXCLUDE USING gist
    ("DoctorId" WITH =, tstzrange("StartAtUtc", "EndAtUtc", '[)') WITH &&)
    WHERE ("Status" <> 'Cancelled')

The half-open interval allows adjacency while preventing intersections for Scheduled, Completed and NoShow records. PostgreSQL arbitrates simultaneous conflicting writes even if they bypass C# validation. Exclusion violations (23P01), concurrency failures and transaction conflicts receive safe retry/reload messages. The application does not automatically retry a non-idempotent booking command.

PostgreSQL's documented btree_gist support covers UUID equality combined with range exclusions; it is a trusted extension available to database roles with CREATE privilege: [PostgreSQL 18 documentation](https://www.postgresql.org/docs/18/btree-gist.html).

## UI

- Book/reschedule forms select existing patients/doctors, local date/time and duration. Active-patient lookup is bounded to 50 matches; search by name or patient number to find others.
- The directory uses 20-row pagination and patient name/number, doctor, date and status filtering/search.
- Details retain cancelled/terminal history and expose only permitted actions.
- Availability publishes future 15-minute starts that can fit the selected duration without a conflict. It is advisory: another user can reserve a displayed time before submission.
- The weekly schedule is a responsive seven-day agenda, restricted to authorized appointments. A 1,000-entry cap requires a doctor filter or the paginated list for unusually busy weeks.
- Existing Bootstrap styling and role-aware navigation are retained.

## Migration and backup

**20261119030236_AddAppointmentManagement was applied to medicare_dev after explicit user approval**, including installation of btree_gist. Before application, the connected database and public schema were reconfirmed, the existing backup passed size/checksum/archive checks again, generated SQL matched the reviewed script, and the pending set contained only this approved migration. No destructive or unexpected operations were found.

Forward SQL is tools/AppointmentManagement-migration.sql. Up installs btree_gist if absent and creates only appointments, restrictive patient/doctor foreign keys, indexes, five check constraints and the doctor/time exclusion constraint. Checks enforce valid statuses, allowed durations/exact end times, finite positive intervals, quarter-hour starts, nonzero booking tokens and cancellation metadata consistency. No existing tables or data are dropped or altered. Down removes appointments and its history; it deliberately retains the extension because other tables may later use it. Do not run rollback casually.

Post-application checks verified btree_gist installation, 13 appointment columns, primary key, two restrictive foreign keys, four B-tree indexes, the GiST exclusion index/constraint, unique booking tokens, all five check constraints and the non-null UUID concurrency column. There are now six applied migrations and no pending migrations. Existing Patient, Doctor, Identity, document and audit tables were compared using row counts and full-row SHA-256 fingerprints and remained unchanged. The baseline contained 1 patient, 1 patient document, 9 audit rows, 3 Identity users, 7 roles and 3 user-role links. Doctor/department/schedule tables had no records. No access to hms_dev was performed.

A new custom-format backup was created outside Git without overwriting previous backups:

- Path: D:\MediCare-Backups\medicare_dev_before_step7_20261119_025724_23678072.backup.
- Size: 32,257 bytes.
- SHA-256: F4EB7B156486DCCE0207D99BF8D6340C3B7BBDBFF1CD9FCDE5716414179E5E10.
- pg_restore --list passed and identified medicare_dev/table data.
- A full offline pg_restore read to Windows NUL passed; no database restore was performed.

The reused helper retains its before_step7 naming prefix; this timestamped file is the verified pre-Step 9 backup. Its size/checksum, pg_restore --list and full offline read all passed again immediately before migration application. A real restore rehearsal has not been performed.

The database now has the schema required by the new build and schedule-edit guard. Restart the local app to load the new module. The existing Debug process was preserved; verification used Release output to avoid its locked files.

## Files

New under src/MediCare.Web:

- Models/Appointment.cs.
- ViewModels/AppointmentViewModels.cs.
- Services/HospitalTime.cs and AppointmentRules.cs.
- Controllers/AppointmentsController.cs.
- Views/Appointments/Index.cshtml, Form.cshtml, Details.cshtml, ChangeStatus.cshtml, Calendar.cshtml, Availability.cshtml and _Rows.cshtml.
- Data/Migrations/20261119030236_AddAppointmentManagement.cs and its Designer.cs companion.

Modified under src/MediCare.Web:

- Data/MediCareDbContext.cs and Data/Migrations/MediCareDbContextModelSnapshot.cs: mapping/constraints/generated snapshot.
- Program.cs: TimeProvider registration.
- Controllers/DoctorSchedulesController.cs: preserve working-hour coverage for booked appointments.
- Views/Shared/_Layout.cshtml and Views/SecurityAuditLogs/Index.cshtml: navigation/event description.

Other new/modified files:

- tests/MediCare.Web.Tests/AppointmentTests.cs and AppointmentPersistenceTests.cs.
- tools/AppointmentManagement-migration.sql.
- tools/MediCare.DatabaseCheck/Program.cs: read-only extension capability and pending migration inspection.
- tools/MediCare.AppointmentDatabaseCheck/MediCare.AppointmentDatabaseCheck.csproj and Program.cs: approved migration preflight/application, schema and preservation verification, rollback-only PostgreSQL workflow/constraint/concurrency probes, and a two-connection exclusion-lock probe when committed patient/doctor records exist.
- docs/APPOINTMENT_MANAGEMENT.md.

## Verification and remaining checks

The final post-migration Release build passed with zero warnings and errors. All **101 tests** passed with zero failures/skips: all 65 existing Patient/Doctor cases plus 36 new appointment cases (24 HTTP, 12 rule/persistence cases). EF reports no model changes since the applied migration and no migrations are pending. Git whitespace checks passed; no commit, push, merge or branch switch occurred. The test runner emitted its existing informational temporary-artifact-directory fallback message; it did not affect results. The separate PostgreSQL two-session probe was skipped for the data precondition described below; it is not included in the 101-test count.

Tests cover successful UTC booking, repeat-submit idempotency, doctor overlap/different durations/adjacency, working-hour gaps, invalid starts/durations, inactive/archived eligibility, past dates, rescheduling, cancellation/history/slot reuse, terminal statuses and end-time boundaries, role permissions, doctor-specific privacy through filters/details/calendar, missing antiforgery tokens, availability, schedule-edit protection, pagination/filter validation and stale appointment updates. The migration's raw exclusion SQL, mapping/indexes and PostgreSQL query translation are inspected without applying it.

Post-migration PostgreSQL integration checks passed for booking, idempotent resubmission, overlapping/different-duration rejection, working-hour checks, adjacent bookings, rescheduling, cancellation/history/slot reuse, early status rejection, Completed/NoShow after end, listing/search/calendar/availability and unlinked-Doctor privacy. Savepoint-isolated SQL probes verified 23P01 overlap rejection, 23505 duplicate token rejection, invalid durations/start intervals/statuses, and invalid patient relationships. Stale EF appointment contexts sharing one PostgreSQL transaction exercised actual row-version failure and atomic rollback of appointment/audit changes. All fixtures and audit events were rolled back; before/after fingerprints of every original table matched.

The independent-session race was **not completed** because the database has no committed doctor records. An attempted uncommitted-parent approach could not supply foreign keys to another session; that probe was rolled back and was replaced with an explicit precondition/skip. No fake clinical records were committed. The reusable checker runs its two-connection locking check when a real committed doctor/patient pair exists: it holds one reservation, verifies an overlapping INSERT waits on the first backend, rolls the first back, observes the second finish and rolls the second back. That check deliberately leaves no bookings behind and does not test committing a winning reservation. Do not describe the currently skipped check as passed.

Repeat the checks from the repository root using `dotnet run --project tools/MediCare.AppointmentDatabaseCheck --configuration Release -- --integration` after registering a real department/doctor and working hours. Without flags the tool verifies schema and existing records only. The --apply-approved path is a database-changing migration command and requires explicit approval; it refuses an unexpected pending migration set.

Direct database writers must still enforce eligibility, working-hour and transition rules; only the application's command paths implement these cross-table/clock-dependent rules. Holidays, leave, date-specific rosters, notifications, payments and clinical record workflows are outside Step 9.

Remaining browser checks: restart the app, register a real department/doctor and working schedule, sign in with real Identity accounts over HTTPS, book from availability, try different durations and overlapping attempts from two tabs/users, reuse a cancelled slot, verify terminal statuses and linked-Doctor visibility, and inspect the visible audit records. Repeat the independent-connection probe once committed doctor data exists.
