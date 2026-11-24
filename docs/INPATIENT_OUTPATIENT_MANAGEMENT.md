# Step 13: Inpatient and Outpatient Management

Implemented in the existing MediCare application, preserving existing modules and uncommitted work. The user explicitly approved the access, inpatient and outpatient policies below. No original hospital stay specification or nursing-assignment model was found in the repository.

## Permissions and privacy

| Role | Permitted new operations |
| --- | --- |
| Administrator | Register/manage wards and beds; aggregate occupancy/availability only. No admission, visit or clinical access from this role. |
| Receptionist | Register admissions/visits, view administrative patient history, transfer beds, cancel Registered outpatient visits, check out doctor-cleared discharges. |
| Doctor | Active linked doctors see only their own assigned admissions/visits; author clinical admission reasons, discharge summaries and outpatient follow-up; approve discharge readiness; start/complete their own visits. |
| Nurse, Laboratory Staff, Pharmacist, Accountant | No new access. Existing independently authorized module permissions remain unchanged. |

Multiple explicitly assigned roles retain their existing union of permissions; Administrator does not imply Doctor or Receptionist. Clinical endpoints require Doctor plus the active linked assigned doctor record, not merely knowledge of an admission/visit ID. Out-of-scope details return 404. Receptionists receive patient names/numbers and administrative information, not diagnoses, admission reasons, discharge summaries, follow-up information, patient contacts or private EMR drafts. Administrator occupancy pages contain counts, not patient identifiers. Reads of admissions, visits and clinical forms are audited before rendering; responses disable caching.

Clinical admission and outpatient notes live in separate tables and are queried only for the assigned Doctor. Existing EMR care checks are unchanged: a stay/visit alone does not confer EMR history access. A clinical note may reference an existing finalized encounter only if it belongs to the same patient and is visible through the existing appointment-based care scope. Reference links are rechecked before display. No new walk-in EMR creation, prescriptions, laboratory results, medicines, billing or automatic appointments are introduced.

Future nursing access requires a separately approved mechanism: explicit `NurseWardAssignment` and/or `NursePatientAssignment` records, active Identity Nurse validation, UTC validity windows, revocation, optimistic versions and grant/revoke audits. Every nursing query would intersect the current assignment with a specifically approved clinical permission; ward membership would never imply unrestricted EMR access. This mechanism is proposed, not implemented or granted by Step 13.

## Wards, beds and inpatient workflow

- Ward codes are trimmed/uppercased and unique. Wards link to existing departments and have names, active status and concurrency versions. Bed numbers are trimmed/uppercased and unique within their ward; beds have operational status and concurrency versions.
- Ward code/department and bed number/ward are permanent identities. Name/status changes are audited and version checked. Occupied wards/beds cannot be deactivated. Records cannot be deleted through the application or stay guards.
- The availability dashboard reports operational, occupied and available beds per ward. Available means operational bed, active ward, active department and no open occupancy. DischargeReady patients still occupy their beds.
- Receptionist admission selects a non-archived existing patient, active account-linked treating doctor in an active department and available operational bed in that department. One active admission per patient is enforced in C# and a PostgreSQL partial unique index. Registration records the current UTC time and responsible account; clinical admission reasons are entered separately by the assigned Doctor.
- Statuses are **Admitted → DischargeReady → Discharged**. No backdating, reopening or automatic deletion is supported. Times display in Asia/Colombo. Patient, doctor, department and admission identities remain immutable.
- Transfers are allowed only while Admitted and require a reason, current admission version and unique action token. They may cross wards within the same treating department; they do not silently reassign the doctor or department. The old occupancy closes at the exact new occupancy start time, and an immutable transfer records previous/new bed, actor, reason and timestamp. The entire transfer, version update and audit are transactional.
- The assigned Doctor first saves the clinical admission reason and discharge summary draft, then explicitly approves discharge readiness. Both are required before clearance. Clearance freezes these clinical notes and preserves the clearing doctor/time. Receptionist checkout then records the discharge account/time, closes occupancy and releases the bed atomically. A second fresh discharge is rejected; a replay of the same completed checkout token resolves to the existing history.
- Draft clinical edits are audited and version checked. Finalized stay notes cannot be rewritten/deleted. Clearance withdrawal, treating-doctor handover/reassignment, backdated corrections and post-clearance amendments are not implemented; they require separately reviewed, history-preserving workflows rather than editing finalized data.

## Outpatient workflow

- Receptionists register a walk-in visit or link a matching Scheduled/Completed appointment. One visit per appointment is enforced even if the visit is later cancelled. Repeated registration tokens do not create duplicate visits; token payload mismatches are rejected.
- Statuses are **Registered → InConsultation → Completed**, or **Registered → Cancelled**. The active linked assigned Doctor starts/completes visits; Receptionists cancel only Registered visits, with a reason. Completed/Cancelled are terminal. Archived patients cannot start a new consultation.
- The assigned Doctor may save optional follow-up information/date and an authorized finalized EMR reference while InConsultation. Follow-up dates cannot be in the past and do not create bookings. Completion freezes follow-up notes.
- Appointment statuses/times are never automatically completed, cancelled or reassigned. Appointment linking validates patient/doctor/status at registration and retains the historical link. Walk-in registration does not create an artificial appointment or EMR encounter.
- Admissions and visits have patient search, doctor/status filtering and 20-row pagination; outpatient date filtering uses the Colombo registration date. Doctors' filters remain scoped to their own linked identity. Patient administrative history is bounded to the latest 50 authorized entries; older entries remain available through the main filtered lists.
- Registration searches up to 50 active patients and shows up to 100 eligible appointment choices, with names/dates rather than requiring manual identifier entry. Clinical EMR references show authorized encounter dates/doctors/revision numbers, not unrelated clinical history.

## Database and migration

Applied migration: **20261119090005_AddInpatientAndOutpatientManagement**, applied to **medicare_dev only** after explicit approval and immediate target, backup checksum/archive, additive-operation and SQL-artifact checks. All eight tables, mapped columns, restrictive foreign keys, indexes, checks, occupancy exclusion and twelve enabled triggers passed post-application inspection. Existing records retained identical full-row SHA-256 fingerprints.

Eight new tables:

- `wards`, `beds`, `admissions`, `bed_occupancies`, `bed_transfers`.
- `admission_clinical_notes`, `outpatient_visits`, `outpatient_clinical_notes`.

Up creates only these tables, their indexes/restrictive foreign keys/checks, new-table guards and a GiST exclusion constraint on bed occupancy. It does not alter existing Patient, Doctor, Appointment, EMR, Laboratory, Pharmacy, Identity or audit tables, update their rows, seed data or install extensions. Existing `btree_gist` is reused. Down refuses automatic destructive rollback.

`EX_bed_occupancies_time` excludes overlapping half-open UTC ranges `[StartedAtUtc, EndedAtUtc)` for the same bed, including open-ended current occupancies. Partial unique indexes also enforce one open occupancy per bed/admission and one non-discharged admission per patient. Deferred consistency triggers require live admissions to have exactly one current bed, discharged admissions to have none, and every occupancy opening/closure to match permanent transfer/checkout history. Transfers close before opening inside a transaction to respect immediate unique indexes. Bed/ward/department and admission row locks coordinate availability, deactivation and allocation. Guid versions protect mutable records; tokens arbitrate repeated admission/transfer/checkout/visit submissions. Conflicts receive safe reload messages rather than exposing patient data or SQL details.

Twelve stay triggers protect facility identity, admission identity/state and discharge clearance, occupancy history, transfer actor/history, outpatient transitions and clinical ownership/finality. Role checks use the server-supplied actor and Identity role membership. These checks do not provide PostgreSQL per-user row-level security against a holder of the shared database owner's credentials; direct database administration remains trusted and must be restricted.

Review artifacts: `tools/HospitalStay-migration.sql` and `Data/HospitalStayDatabaseSql.cs` (freeze the migration implementation after application). Existing EMR finalization/amendment triggers and all previous migrations are untouched.

## Backup and preflight

Fresh PostgreSQL custom-format backup outside Git, without overwriting an existing archive:

`D:\MediCare-Backups\medicare_dev_before_step7_20261119_085915_e9f4cda4.backup`

- Size: **118,841 bytes**.
- SHA-256: `ABC138D97A0653F18DED2CBFD961DD39118A2045C46129DCAF007B0135AB33C9`.
- `pg_restore --list` identified medicare_dev/table data and passed.
- A full offline pg_restore read to Windows NUL passed; no database restore was performed.
- Read-only `--stay-preflight` reconfirmed the actual database is medicare_dev, the exact sole pending migration, additive operations, restrictive FKs, existing btree_gist, absence of new-object collisions, SQL artifact equivalence, unchanged checksum and archive readability. `hms_dev` was not accessed.
- Reverify backup freshness and preflight immediately before approved application. A restore rehearsal and separate backup of private patient-document files remain outside this database-only backup.

## Verification and outstanding checks

Post-application verification: Release build passes with zero warnings/errors. All **276 automated tests pass**, including all previous 229 and 47 new stay tests. Backup checksum and both archive listing/full offline read were reverified immediately before application. No existing hospital records were changed by migration or verification.

The automated suite includes all previous 229 tests plus stay HTTP/Razor authorization/CSRF, clinical privacy, active-patient/doctor/bed rules, duplicate admission/bed prevention, idempotency/payload mismatch, within/cross-ward transfers and invalid transfers, required doctor clearance, checkout and bed release, immutable terminal workflows, outpatient follow-up/cancellation/appointment linking, unchanged appointment/EMR behavior, ward/bed management, six EF concurrency-token cases, PostgreSQL availability query translation and additive migration/guard inspection.

InMemory test transactions are explicitly ignored in the isolated test fixture; those HTTP tests do not prove PostgreSQL exclusion, deferred checks or atomic rollback. The guarded `--integration-stay` provider checks now pass against PostgreSQL: admission/idempotency/active duplicate refusal, transfer/history, clinical draft/clearance, checkout/release, outpatient consultation/follow-up/completion and terminal/history guards. Additional checks pass for occupied-bed deactivation rejection, unauthorized clearance, deferred incomplete-transfer rollback, duplicate occupancy refusal and stale-version rejection using separately tracked EF contexts. All synthetic clinical/Identity/audit fixtures were enclosed in an outer transaction and rolled back; all table fingerprints, including the eight empty new tables, matched afterward.

Remaining provider checks include independently isolated exclusion conflicts, the full duplicate ward/bed/token matrix, inactive/archived eligibility and unauthorized clinical author/reference attempts beyond HTTP coverage. Independent backend sessions must race different patients for one bed and competing transfers/discharges; separately tracked contexts sharing one rollback transaction are not simultaneous-session proof. Existing committed doctor/admission fixtures were not fabricated merely to support those tests. Database role guards validate supplied actor membership; shared database-owner credentials remain trusted and are not per-user row-level security.

Browser acceptance after migration and application restart: Administrator aggregate-only ward/bed pages; Receptionist admission/transfer/checkout and outpatient registration/cancellation without clinical text; assigned/unrelated/unlinked Doctor scopes; clinical draft and discharge-ready freeze; outpatient start/follow-up/completion; named patient/doctor/appointment selection; filters/pagination, mobile layout, validation, double-click retries, bed availability refresh and audit screens. No existing accounts are automatically reseeded or granted roles.

## Files created and modified

- Added `Models/HospitalStay.cs`, `Data/HospitalStayMapping.cs`, `Data/HospitalStayDatabaseSql.cs`, `ViewModels/HospitalStayViewModels.cs`.
- Added `Controllers/HospitalCareControllerBase.cs`, `AdmissionsController.cs`, `OutpatientsController.cs`, `FacilitiesController.cs`.
- Added shared `Views/HospitalCare` list/registration/details/clinical forms and `Views/Facilities` ward dashboard/bed list/administration forms.
- Updated `Data/MediCareDbContext.cs`, `Controllers/ClinicalAdministrationControllerBase.cs` (safe exclusion-conflict handling), and `Views/Shared/_Layout.cshtml`.
- Added the stay migration/designer, updated EF snapshot, and added the SQL artifact.
- Added `HospitalStayTests.cs`, `HospitalStayPersistenceTests.cs`, `tools/MediCare.DatabaseCheck/HospitalStayPreflight.cs` and `HospitalStayDatabaseChecks.cs`; updated tool `Program.cs` routing.
- Added this document. No commits, pushes, merges or branch switches performed.
