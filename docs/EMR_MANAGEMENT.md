# Step 10: Electronic Medical Records

Implemented in the existing MVC application. No new project, credentials, roles, clinical fixtures in medicare_dev, or Git mutations are required. Existing Patient, Doctor and Appointment workflows remain in place.

## Approved clinical access policy

The repository has no original hospital clinical-access specification. The user explicitly approved this conservative policy on 2026-11-19:

- Only a signed-in Doctor linked to an active Doctor record can access clinical records.
- A Scheduled or Completed appointment with that doctor establishes access to that patient's finalized history, including other treating doctors' finalized revisions. This policy currently has no expiry window; even a future Scheduled appointment establishes the relationship.
- Drafts are visible only to their original author, while that author remains linked to the active treating doctor and has an eligible care relationship.
- Creating an encounter requires the doctor's own Scheduled or Completed appointment and a non-archived patient. One encounter is allowed per appointment. Walk-in/unassigned encounters are not supported under this approved initial policy.
- Editing, finalizing and amending require the original author account, original active linked doctor and original appointment still assigned to that doctor with Scheduled or Completed status. Another doctor's care relationship does not grant write access.
- Administrator, Receptionist, Nurse, Pharmacist and other staff roles do not themselves grant clinical access. As with existing Identity authorization, an account explicitly holding Doctor as well as another role is evaluated through its Doctor identity and care relationship.
- Cancelling an appointment, marking it NoShow, unlinking/deactivating a doctor or removing the Doctor role can revoke clinical access. Other eligible appointments may still authorize finalized history. Archiving a patient prevents new encounters but does not erase their history or prevent authorized corrections.

The clinical query scope is applied before search, patient filtering, listing, details, printing and PDF export. Unauthorized patient/record IDs return 404. No Patient controller permissions were broadened. Patient profiles display a restriction notice; doctors use their appointments and the Medical Records navigation. Receptionists do not receive diagnoses or clinical-history counts through patient administration.

## Records and history

An encounter stores immutable patient/appointment/doctor/author identity, creation time, an idempotency token and a concurrency version. It has sequential revisions containing symptoms, observations, diagnosis and treatment notes, with up to 20 structured prescriptions. Each prescription stores medication, dosage, route, frequency, duration and optional instructions. This is documentation only: no dispensing, drug-interaction advice, laboratory ordering or billing workflow is added.

Draft fields may be incomplete. Partial prescriptions are rejected; leave unused prescription rows completely blank. Finalization requires symptoms, diagnosis and treatment. Finalizing is an explicit antiforgery-protected POST. The revision and its prescriptions then become immutable. A correction starts a new draft copied from the latest finalized revision and requires a reason. The earlier revision remains available and is never silently rewritten. Only one draft revision may exist per encounter. An amendment must itself be explicitly finalized before other authorized care doctors can see it.

Every revision shows its number, author account, treating doctor and timestamps. History includes prior finalized revisions; these are labelled by revision number rather than silently replaced with the latest text. Search uses patient name/number, optional patient scope, local revision date and Draft/Finalized status. Results use 20-row pagination. All timestamps are stored in UTC and displayed in Asia/Colombo.

No delete endpoint exists. Every write stages its audit event in the same SaveChanges transaction. Reads, history views, print views and PDF exports also audit successful access before delivering content. Events record account/entity identifiers and action names without clinical text, medication, patient names or search terms. Failure to save a read/export audit prevents delivery. Existing application logging keeps Microsoft.AspNetCore at Warning; deployment proxies and logging changes must also keep clinical request bodies and patient-name query strings out of access logs.

Guid optimistic concurrency protects encounters and revisions. Edit forms post the revision version; stale edits/finalizations fail with reload guidance. Amendments contend on the encounter version and sequential revision uniqueness. Writes rotate the appointment and doctor versions to coordinate against cancellation, reassignment, unlinking or deactivation; creation also rotates the patient version to coordinate archival. These token changes do not change appointment status, times or clinical identity. Repeated create submissions open the existing encounter rather than creating another; unique appointment/token indexes also arbitrate simultaneous submissions.

## Reports

Printable HTML and downloadable PDFs use the same care/draft access rules as Details and disable response caching. PDFs are generated in memory, served as attachments with an identifier-only filename and nosniff, and never written to wwwroot or public storage. Draft reports are visibly marked DRAFT — NOT FINALIZED. Printed/downloaded copies leave application access control; handle them as confidential clinical documents.

PDF rendering uses pinned PDFsharp-MigraDoc 6.2.4 (MIT; see [package license](https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4)) and an explicit patched System.Security.Cryptography.Pkcs 10.0.15 dependency. Later PDFsharp releases have different licensing; do not upgrade without reviewing the package license. The current Windows deployment uses its installed Arial font and embeds PDF font subsets. Other operating systems require a licensed font resolver. The PDF renderer does not support complex-script shaping: the UI labels its download as Latin script, and the server refuses unsupported script content with a generic 422 response rather than silently emitting missing/mis-shaped clinical text. Sinhala/Tamil and other scripts remain available in the authorized printable HTML view, which supports browser font fallback and Save as PDF. A multilingual server PDF renderer is an outstanding enhancement. Reports are not digitally signed or PDF/A certified.

## Database migration and backup

**20261119043722_AddMedicalRecords was applied to medicare_dev after explicit user approval.** Immediately before application, the actual target/schema, backup size/checksum/archive, sole pending migration, reviewed SQL and absence of object-name conflicts were reconfirmed. No unexpected destructive operations were found. There are seven applied migrations and none pending.

Up creates only medical_records, medical_record_revisions and medical_prescriptions, their restrictive foreign keys/indexes/checks, and three trigger functions/triggers on these new tables. It does not alter existing tables or data, install extensions, or seed clinical data. Foreign keys retain patients, appointments, doctors and author Identity accounts. Unique indexes enforce one encounter per appointment, unique creation tokens, sequential revision identities, one draft per encounter and ordered prescription positions. Checks protect token, revision/amendment/finalization and prescription integrity.

New-table PostgreSQL triggers verify initial encounter identity against an eligible care relationship, protect encounter identity and revision author/sequence, reject encounter/revision deletion, and prevent changes to finalized revisions or their prescriptions. Prescription writes lock their draft parent to coordinate against concurrent finalization. Application role/patient authorization remains essential: these triggers do not turn a shared application database account into per-user PostgreSQL row-level security. Direct database operators remain privileged. PostgreSQL execution checks passed after application, as detailed below; InMemory tests alone do not validate triggers.

Down removes the new EMR tables and trigger functions, losing EMR history. Do not run rollback casually. The applied forward SQL is tools/MedicalRecords-migration.sql.

Post-application verification confirmed medicare_dev, all three new tables (30 columns), three primary keys, seven validated restrictive foreign keys, four checks, nine indexes plus primary-key indexes, the one-draft partial unique index, two UUID concurrency columns and three enabled row-level history triggers. Every expected column/nullability, named index and foreign-key target matched the approved migration. No EMR triggers were attached to existing tables. No access to hms_dev was performed.

Fresh custom-format backup, created without overwriting existing backups:

- Path: D:\MediCare-Backups\medicare_dev_before_step7_20261119_044205_abe286b5.backup
- Size: 38,123 bytes.
- SHA-256: D880A5A084B1B0EB59A0F436DA13F63C44125AC54EC79172488AB897F85546D7.
- pg_restore --list: passed and identified medicare_dev/table data.
- Full offline pg_restore read to Windows NUL: passed; no database restore performed.

The reused backup helper retains its before_step7 prefix. This timestamped archive is the pre-EMR backup. Its size/checksum, pg_restore --list and full offline read passed again immediately before application. Database backup does not include private patient-document filesystem content; coordinate that store's backup for disaster recovery. No actual restore rehearsal has been performed.

## Verification and remaining checks

The automated suite uses isolated EF InMemory stores and real MVC/Razor TestHost requests with test-only authentication, plus PostgreSQL model/migration/query inspection. Separate post-application PostgreSQL probes exercise the real controllers/provider inside an outer transaction that is always rolled back. All clinical and Identity fixtures are explicitly synthetic; no fictional clinical records, temporary accounts or audit events were committed to medicare_dev.

After migration application, `dotnet build --configuration Release --no-restore` succeeded with zero warnings/errors and `dotnet test --configuration Release --no-build --no-restore` passed the entire **128-test suite**, zero failures/skips: all 101 prior Patient/Doctor/Appointment cases plus 27 EMR cases. EMR checks include real MVC/Razor authorization and antiforgery, linked/unrelated/unlinked/inactive doctor scope, revoked relationships, private drafts versus shared finalized history, structured prescription validation, successful and repeated creation, draft edits and stale submissions, finalization, required-reason amendments and retained history, blank-draft finalization rejection, filters, authorized private PDF/print delivery, unsupported-script PDF refusal, multi-page reports, EF concurrency failures, PostgreSQL query translation and additive migration/guard inspection. The test runner's existing temporary-artifact-directory fallback message remains informational. No application or approved-migration corrections were necessary; the existing Debug process was preserved, so restart the local app to load the new module.

`dotnet ef migrations has-pending-model-changes` reported no changes since the new migration. The pre-application read-only --emr-preflight passed for the actual target, sole pending migration, additive operations, SQL equivalence, object names and backup verification. Existing PostgreSQL patient list/archive/profile/edit/document read checks also passed. The explicitly approved --apply-approved-emr command then applied only AddMedicalRecords after repeating all preflight checks.

Post-application PostgreSQL checks passed for creation, structured prescriptions and prescription replacement, retrieval/PDF export, duplicate-submit protection, unrelated linked-doctor denial, stale edit forms, finalization, reasoned amendments and duplicate-amendment rejection. The original finalized revision/prescriptions were preserved through an edited and finalized amendment. Savepoint-isolated SQL probes verified rejection of finalized revision updates/deletion, finalized prescription updates/deletion/insertion, encounter identity changes/deletion, mismatched encounter author, invalid revision sequencing, draft-author changes, incomplete finalization, duplicate prescription positions and blank prescription fields (expected SQLSTATE 23514 or 23505).

Two EF contexts sharing the rollback transaction verified an actual PostgreSQL stale revision failure. A staged failed audit event and every row were fingerprinted before/after to confirm atomic rollback. This tests stale-version conflict handling, not simultaneous independent backends. Independent-session amendment/finalization locking tests remain unperformed: no committed Doctor or EMR records exist, and no fake clinical fixtures were committed to enable them. Role and antiforgery filters run in the HTTP suite; the PostgreSQL controller probes verify patient-level scope using linked test Doctor identities, without pretending direct controller invocation executes MVC role filters.

Before/after full-row SHA-256 fingerprints and counts matched for every pre-existing application table: 1 patient, 1 patient document, 9 security audit events, 3 Identity users, 7 roles, 3 user-role links, and zero departments/doctors/schedules/appointments. The other four Identity tables were empty and unchanged. All fixtures and token updates were rolled back. A separate schema/read verification confirmed all three new EMR tables remained empty, with no pending migrations. The PostgreSQL probes are separate from the 128-test suite count.

Repeat schema/read verification: `dotnet run --project tools/MediCare.DatabaseCheck --configuration Release --no-restore -- --verify-emr`. Repeat rollback-only workflow checks: replace --verify-emr with --integration-emr. The --emr-preflight mode is for the pre-application state and correctly refuses once the migration is applied. Never use --apply-approved-emr without explicit approval; it refuses any unexpected pending-migration set and cannot reapply the already-applied migration.

Remaining browser acceptance: restart the existing local app after the schema migration; use real Identity accounts over HTTPS; verify treating/unrelated/unlinked/inactive Doctor visibility, non-clinical roles denied, draft edits and two-tab conflicts, duplicate submissions, finalization and reasoned amendments, longitudinal history, prescription fields, search/pagination, appointment/profile links, printed page breaks and PDF text/font fidelity. Inspect audit events without clinical text. Recheck cancellation/reassignment/account-link revocation. Existing Step 8/9 manual checks and the previously skipped independent-session booking probe still remain as documented in those modules.

Operational production requirements include encrypted transport/storage and backups, restricted database access, retention/release-of-information policy, monitoring, controlled access reviews and deployment-specific log handling. This development module is not a claim of regulatory certification.

## File inventory

Added under src/MediCare.Web: Models/MedicalRecord.cs; Data/MedicalRecordMapping.cs; Services/MedicalRecordAccess.cs and MedicalRecordPdf.cs; ViewModels/MedicalRecordViewModels.cs; Controllers/MedicalRecordsController.cs; Views/MedicalRecords/Index, Form, Details, Print and _Report.cshtml; the AddMedicalRecords migration and Designer.

Modified: Data/MediCareDbContext.cs and its model snapshot; MediCare.Web.csproj for PDF dependencies; Views/Shared/_Layout.cshtml; Views/Appointments/Details.cshtml; Views/Patients/Details.cshtml; Views/SecurityAuditLogs/Index.cshtml.

Other additions: tests/MediCare.Web.Tests/MedicalRecordTests.cs and MedicalRecordPersistenceTests.cs; tools/MedicalRecords-migration.sql; tools/MediCare.DatabaseCheck/MedicalRecordPreflight.cs and MedicalRecordDatabaseChecks.cs; this document. The existing DatabaseCheck Program.cs retains its default read-only behavior and accepts explicit EMR preflight, approved-application, verification and rollback-only integration flags described above. No new project was created. Earlier uncommitted work is preserved. This application/verification turn changes only the existing checker Program.cs, the new MedicalRecordDatabaseChecks.cs and this report; the approved migration and application code required no correction.
