# Step 11: Laboratory Management

Implemented in the existing MediCare MVC application. No new project, roles, passwords, seeded clinical results or Git mutations are introduced. Earlier Patient, Doctor, Appointment and EMR work is preserved. No original hospital laboratory specification was present; the following development rules document the implementation and the user's explicit permission decision.

## Approved permissions

The user approved this policy on 2026-11-19:

| Capability | Administrator | Treating Doctor | Laboratory Staff | Other roles |
| --- | --- | --- | --- | --- |
| Manage catalogue | Yes | No | No | No |
| Request tests for own eligible encounters | No | Yes | No | No |
| View own requests and finalized results for patients in authorized care | No | Yes | Laboratory workflow only | No |
| Collect, receive and process samples | No | No | Yes | No |
| Enter a new result draft | No | No | Yes, for an InProgress request | No |
| Edit/finalize/correct a result | No | No | Original result author only | No |
| Cancel an unfinished request, with a reason | No | Own uncollected request only | Yes | No |
| View unrestricted patient EMR histories | No additional permission | Existing EMR care scope | No | No |

Role permissions follow existing Identity role membership. Holding Administrator does not grant clinical access. An account explicitly assigned multiple roles can exercise each assigned role; no role grants another role automatically. Receptionist, Accountant, Nurse and Pharmacist do not gain laboratory result access.

Doctors must remain linked to an active Doctor and have a Scheduled or Completed appointment for the patient, following MedicalRecordAccess. New requests additionally require their own original encounter/author, the original appointment still assigned to that doctor and eligible, and a non-archived patient. A doctor cannot order against another author's encounter merely because they can read its finalized history. Read scope gives doctors their own eligible requests and other treating doctors' requests only once finalized results exist. Result drafts and correction drafts are private to their Laboratory Staff author. Other care doctors can read finalized revisions, including superseded history, without gaining edit access.

Laboratory Staff get only patient number/name, requesting doctor, ordered test snapshot, sample tracking and laboratory results. Worklist/details/report projections do not include patient DOB, contacts, symptoms, observations, diagnosis, treatment or prescriptions. No Patient or EMR controller authorization was broadened. Every clinical route/export requires Doctor or Laboratory Staff authorization; extra action rules restrict ordering to Doctors and processing/results to Laboratory Staff. Unauthorized record/result IDs return 404.

## Catalogue and requests

Administrators create/search/page/edit/activate/deactivate tests with a normalized unique code, name, category, description, sample type, Numeric/Text format and optional configured unit/reference-range text. There are no invented test definitions, measurements, ranges or interpretations. Catalogue changes are audited and version-protected; no deletion endpoint exists.

A request selects one active test for an existing eligible encounter. The request stores an immutable snapshot of code, name, category, sample type, result format, unit and reference range. Future catalogue changes affect future requests without rewriting previous reports. Deactivation does not invalidate a test already ordered. Request submission carries a unique creation token; an identical repeat opens the saved request, while reuse for a different payload is rejected. The database token index also prevents simultaneous duplicate submissions. Separate deliberate requests for the same test are permitted; token idempotency does not impose an invented clinical repeat-testing restriction.

Requests link to MedicalRecord, which already fixes the patient, appointment and treating doctor. Restrictive foreign keys preserve those relationships. Request creation rotates the catalogue, encounter, patient, doctor and appointment versions to coordinate against catalogue deactivation, patient archival, doctor unlinking and appointment reassignment/cancellation. Clinical content and appointment/EMR statuses are not changed.

## Sample workflow

The normal sequence is Requested → SampleCollected → InProgress → Completed. Receipt is a separate action while SampleCollected; processing requires receipt first. Collection creates a server-generated LAB- identifier with 32 hexadecimal characters. A unique index protects identifiers. Collection, receipt and processing each record UTC time and responsible Identity account. Display uses Asia/Colombo. These are recorded action timestamps; backdating or later custody edits are not supported.

Requested, SampleCollected and InProgress may be cancelled under the approved permissions, with a required reason, timestamp and actor. Cancellation retains the request, sample identifiers, custody trail and any existing draft. Completed/Cancelled are terminal. Completed requests remain Completed during result correction. Repeating or racing a workflow action fails its version/state checks rather than generating another sample or replaying a transition. Existing requests may continue through the restricted laboratory workflow if the patient is later archived or the requesting doctor becomes inactive; those changes do not erase or silently cancel investigations.

Listing supports patient name/number, test code/name, sample identifier, status, local request date, patient and encounter scope, with 20-row pagination. Laboratory Staff have an open worklist excluding Completed/Cancelled by default and can explicitly search history. Doctors reach their patient's lab history from an authorized medical record and start investigations from their own encounter. Navigation remains MediCare-branded and role-aware.

## Results and reporting

The first draft requires InProgress status. Numeric values support up to 20 integer digits and 8 decimal places; negative values are allowed without inventing clinical bounds. Text results support up to 4,000 characters. Optional comments support 2,000 characters. Format must match the ordered snapshot, and numeric/text values cannot be combined. Drafts may be incomplete, but finalization requires a value in the configured format. Units/reference ranges are taken from the snapshot, never accepted as result-form overrides. No abnormal flags or medical interpretations are generated.

Only the result author can edit/finalize their draft. Finalization records its timestamp and atomically completes the request. A correction requires the latest finalized revision, original author and a reason; it copies the prior value/comments to a new private draft with a sequential number. The previous revision stays immutable and visible in history. Finalizing the correction publishes the new revision without overwriting the original completion time or previous result. One draft per request is permitted. There is no deletion endpoint or staff takeover path. If the original author is unavailable, correction requires a separately approved access/workflow policy change; Administrator is not an automatic clinical override.

Only finalized revisions can be printed/exported. Every report/download uses the same patient/role scope as the request and disables caching. PDFs are built in memory using existing PDFsharp/MigraDoc and the shared Windows Arial font setup, served as attachments with ID-only filenames and nosniff, and never placed in public storage. Latin-script limitations match EMR: unsupported script content gets a generic 422 rather than a misleading PDF; the authorized browser print view supports Sinhala/Tamil and browser Save as PDF. Reports identify revision, author, finalization/correction and sample custody. They are not digitally signed or PDF/A certified.

Mutations require POST, antiforgery, server validation and Guid concurrency tokens. Entity changes and audit events save together. Requests, collection/receipt/processing/cancellation, result drafts/finalization/corrections, catalogue changes and sensitive list/detail/report access are audited with IDs/action names, without values, test comments, diagnoses, patient names or search text. Read/export audit failures prevent content delivery. Existing logging configuration suppresses routine ASP.NET request logs; deployment logs must likewise avoid clinical bodies and patient-name search strings.

## Migration and backup

**20261119053856_AddLaboratoryManagement was applied to medicare_dev after explicit user approval.** Immediately before application, the actual database/schema, backup checksum/size/archive, sole pending migration and forward SQL were reverified. No unexpected destructive operations or object-name conflicts were found. Eight migrations are now applied and none remain pending.

Up adds only lab_tests, lab_requests and lab_result_revisions, their indexes, nine restrictive foreign keys, fourteen check constraints and new-table trigger functions/triggers. No existing tables/data or extensions are altered. Forward SQL: tools/LaboratoryManagement-migration.sql. The reviewed guards protect request identity/test snapshots, permitted status transitions, recorded sample custody, sequential author-owned corrections and immutable finalized results. Two deferred constraint triggers enforce that a Completed request has finalized results and finalized results belong to Completed requests, checking the final transaction state so EF can save the parent and revision in either order. All four triggers attach only to new laboratory tables; none attach to existing Patient/Doctor/Appointment/EMR/Identity tables.

PostgreSQL role/patient authorization remains an application responsibility; a shared privileged database login is not per-user row-level security. The guards add workflow/history integrity, not authorization for arbitrary SQL operators. Trigger execution, deferred consistency and stale-write behavior passed the post-application PostgreSQL checks below. Deferred constraints were explicitly flushed with SET CONSTRAINTS ALL IMMEDIATE in the rollback-only tests; a rollback alone would not exercise commit-time checks. Down removes the new lab history/tables/functions and must not be run casually.

Read-only preflight confirmed medicare_dev, PostgreSQL 18.3, the seven prior applied migrations and exactly the approved pending laboratory migration. It verified additive operations, expected relationships/checks, no new-object name conflicts, SQL equivalence to the reviewed migration and backup integrity. Post-application verification matched every laboratory column/nullability, primary key, restrictive foreign-key target, check constraint, named index, uniqueness/partial-index setting and all four enabled row triggers, including both deferrable/initially-deferred completion triggers. No new laboratory triggers attach to existing tables. EF reports no model changes since the new migration. No access to hms_dev was performed.

Fresh custom-format backup outside the repository, without overwriting older archives:

- Path: D:\MediCare-Backups\medicare_dev_before_step7_20261119_054513_4e010026.backup
- Size: 56,284 bytes.
- SHA-256: F0AF62E77F1F22DC4BAB8398774283461AAE13333791EDCBCFE95DEC635B1EEF.
- pg_restore --list passed and identified medicare_dev/table data.
- Full offline pg_restore read to Windows NUL passed; no restore was performed.

The existing backup helper retains its before_step7 prefix; this timestamped archive is the pre-Laboratory backup. Checksum, size and both archive checks passed again immediately before application. Database archives do not contain private patient-document filesystem content; a coordinated file backup and a real restore rehearsal remain separate requirements.

## Verification and remaining checks

The automated suite uses isolated EF InMemory stores and real MVC/Razor TestHost requests with test-only authentication, plus PostgreSQL query/model/migration inspection. Separate post-application PostgreSQL probes use the real provider/controllers inside an outer transaction that is always rolled back. All fixtures/values are explicitly synthetic; no invented clinical data, temporary Identity accounts or audit events were committed to medicare_dev.

After migration application, `dotnet build --configuration Release --no-restore` passed with **zero warnings and errors**, and `dotnet test --configuration Release --no-build --no-restore` passed the complete **180-test suite**, zero failures/skips: all prior 128 Patient/Doctor/Appointment/EMR cases plus 52 laboratory cases (32 HTTP cases and 20 rules/persistence cases). Tests cover catalogue validation/permissions/versioning, clinical role boundaries, antiforgery, doctor care scoping, request idempotency and catalogue snapshots, inactive/archived eligibility, collection/receipt/processing order, repeated/stale actions, restricted staff projections, numeric/text formats, private drafts, finalization, author-only corrections and retained history, cancellation permissions/history, reporting/download authorization, Unicode PDF refusal, filtering/pagination, EF concurrency conflicts and migration/SQL integrity definitions. The initial implementation worklist view-routing defect was corrected; a search-scope assertion was corrected to distinguish user-entered search text from returned patient rows. No failures remain. The test runner retains its existing informational temporary-artifact-directory fallback message. EF's post-application model check reported no changes since the migration. The user-started application process was preserved; restart it to load the laboratory module against the migrated schema.

Post-application PostgreSQL checks passed for request and result-draft idempotency; collection/receipt/processing sequence and custody; invalid/stale actions; numeric and text entry/finalization; private drafts; original-author corrections and duplicate-correction rejection; unchanged prior values/finalization timestamps/original request completion; cancellation permissions and retained sample history; patient-level Doctor scope; unrelated-doctor ordering/report denial; other-staff edit/finalization/correction denial; and authorized report/PDF delivery. These direct controller probes test patient/author scope; MVC role and antiforgery filters are exercised by the HTTP test suite, not by direct controller invocation.

Fourteen savepoint-isolated SQL probes passed for finalized result updates/deletion, snapshot mutation, terminal reopening, custody overwrite, duplicate request tokens/sample IDs, status jumps, wrong result format, result-author mutation, nonfinite numeric values, duplicate/sequential drafts and both deferred completion directions. Deferred rejection tests also verified the exception originated from medicare_lab_completion_check, so an earlier timestamp/value constraint cannot falsely satisfy those cases. Test SQL uses clock_timestamp() where action timestamps must follow records created after the outer transaction began.

Two EF contexts sharing the rollback transaction verified actual PostgreSQL stale result and request conflicts. A staged failed audit event and complete before/after table fingerprints verified atomic rollback of the failed result/audit write. The probes also confirmed laboratory actions did not finalize EMR records or change appointment status. This is stale-version testing, not simultaneous independent backends. Independent-session workflow/finalization/correction races remain unperformed because no committed Doctor, encounter or laboratory fixtures exist; no fake clinical rows were committed to enable that testing. Earlier Step 8/9/10 independent-session limitations remain documented.

Full-row SHA-256 fingerprints and counts matched for every original application table before/after migration and after all fixture rollback: 1 patient, 1 patient document, 9 audit records, 3 Identity users, 7 roles and 3 user-role links. Other Identity tables and Doctor/Department/Schedule/Appointment/EMR tables were empty and unchanged. All three laboratory tables were confirmed empty after rollback. No original records or audit events changed.

Repeat schema/read checks: `dotnet run --project tools/MediCare.DatabaseCheck --configuration Release --no-restore -- --verify-lab`. Repeat rollback-only PostgreSQL probes: replace --verify-lab with --integration-lab. The --lab-preflight mode is for the unapplied state and correctly refuses once applied. The explicit --apply-approved-lab command requires approval and refuses any unexpected pending set, including attempting to reapply this already-applied migration. Default DatabaseCheck behavior remains read-only.

Browser acceptance after migration: restart the app; use real Identity accounts over HTTPS; configure real catalogue entries as Administrator; request a test as the linked treating Doctor; collect/receive/process as Laboratory Staff; enter and finalize a result; verify author-only reasoned corrections, history, cancellation and two-tab conflicts; verify other Doctors and all unrelated roles are denied; inspect patient/encounter links, worklist/search/pagination, reports/PDF fonts/page breaks and visible audit events. Confirm laboratory pages never expose EMR notes or patient contacts. No application restart or user process termination was performed automatically.

Scope limitations: one test/sample and one numeric or text value per request; no multi-analyte panels, recollection/sample rejection, barcode hardware, analyzer interfaces, external laboratory transfers, clinical interpretation, notifications, billing, pharmacy or laboratory staff takeover workflow. These require further specification rather than inferred clinical behavior.

## Files

Added under src/MediCare.Web:

- Models/Laboratory.cs; Data/LaboratoryMapping.cs.
- ViewModels/LaboratoryViewModels.cs.
- Services/LaboratoryRules.cs, LaboratoryAccess.cs, LaboratoryPdf.cs.
- Controllers/LabTestsController.cs and LaboratoryController.cs.
- Views/LabTests/Index.cshtml and Form.cshtml.
- Views/Laboratory/Index.cshtml, RequestForm.cshtml, Details.cshtml, ResultForm.cshtml and Report.cshtml.
- Data/Migrations/20261119053856_AddLaboratoryManagement.cs and Designer.cs.

Modified under src/MediCare.Web: Data/MediCareDbContext.cs and the generated model snapshot; Services/MedicalRecordPdf.cs to share its font initializer/text support; Views/Shared/_Layout.cshtml, Views/MedicalRecords/Details.cshtml and Views/SecurityAuditLogs/Index.cshtml. Existing EMR report content and permissions are retained and regression-tested.

Other additions: tests/MediCare.Web.Tests/LaboratoryTests.cs and LaboratoryPersistenceTests.cs; tools/LaboratoryManagement-migration.sql; tools/MediCare.DatabaseCheck/LaboratoryPreflight.cs and LaboratoryDatabaseChecks.cs; this report. The existing DatabaseCheck Program.cs accepts explicit laboratory preflight, approved application, schema verification and rollback-only integration modes. MedicalRecordDatabaseChecks.cs exposes its existing connection-context/snapshot/preservation helpers internally for reuse, without changing EMR verification behavior. No separate project was created and no existing uncommitted work was discarded. Application/migration code required no correction during the approved application/verification turn.
