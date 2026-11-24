# Step 7 implementation and verification

Patient profiles retain personal details, emergency contacts and the pending EMR integration notice. Registration's Razor form was repaired because the existing Create.cshtml contained view-model C# instead of a form.

## Documents

Administrator and Receptionist can upload and download documents for active patients. Administrators can also download documents for archived patients. Uploads require antiforgery validation and matching PDF/JPEG/PNG extension, declared MIME type and header signature. The file limit is 5 MiB (5,242,880 bytes); the multipart request limit is 6 MiB. Signatures identify formats; they do not establish that a document is safe.

Files receive random, extensionless identifiers and live under the application's App_Data/PatientDocuments directory, outside wwwroot. That directory is ignored by Git. Never configure static-file middleware or a reverse proxy to serve it. Original names are display/download metadata only. Downloads require both patient ID and document ID, return attachments, disable caching and set nosniff. Audit targets for patient and document events are record GUIDs in the existing TargetUserId column; no patient names or file contents are logged.

Metadata, patient concurrency changes and upload audit events save together in EF Core's transaction. Ordinary write failures delete the newly written file. Download audit failures prevent delivery. A process crash between writing a file and committing metadata, an ambiguous connection failure at commit, or filesystem failure during cleanup can still leave an orphan or missing file: filesystem and PostgreSQL cannot share an atomic transaction. During maintenance, stop uploads, compare stored identifiers with patient_documents, investigate differences, and back up both stores before removing confirmed orphan files. Do not infer orphan status from filename age alone.

Production requires malware scanning/quarantine before documents become downloadable, restrictive filesystem permissions, upload rate limits/quotas, monitoring, encrypted storage and coordinated database/file backups. No malware scanner is integrated in this step.

## Archiving and concurrency

Only Administrators can list archived patients, confirm archiving and reactivate. Changes use antiforgery-protected POST actions. Active searches and edit actions exclude archived patients. Archived records retain IDs, patient numbers and documents.

Patient Version is a Guid concurrency token. Edits post their original token; archive/reactivate posts also require the current token. Successful edits, archive/reactivate and uploads rotate it. Concurrent changes produce an error rather than overwriting another change. Existing rows receive a nonzero initial token through the migration, then rotate independently on subsequent writes. All patient changes save their audit event in the same SaveChanges call.

## Database safety and deployment

The read-only probe in tools/MediCare.DatabaseCheck loads existing development configuration without printing secrets, refuses any configured database other than medicare_dev, and checks current_database() inside a read-only transaction. The web app also refuses another configured database name. The development database-check endpoint now requires Administrator authorization.

Created migration: 20261118171711_AddPatientDocumentsAndConcurrency. Its Up adds the Version column, patient_documents table, indexes and a restrictive foreign key; it does not drop existing objects or data. The generated forward SQL is tools/Step7-migration.sql. Down removes document metadata and must not be run casually.

Migration 20261118171711_AddPatientDocumentsAndConcurrency was applied to medicare_dev after explicit user approval. Before applying it, the actual connected database was reconfirmed, the generated SQL was compared with the reviewed script, and the verified custom-format backup at D:\MediCare-Backups\medicare_dev_before_step7.backup was checked again. Backup size: 21,372 bytes; SHA-256: C76B0939C49C3C083F2721B1ECD57DC6D870467CB7E8DB09DAB0FEF6D901128D. Both pg_restore --list and an offline full archive read to Windows NUL passed; no database restore test was performed.

Post-application checks confirmed the document table, primary key, patient-ID index, unique stored-filename index, validated restrictive foreign key, and non-null uuid patient Version column. Row counts and SHA-256 fingerprints of every existing column were compared before and after: the one existing patient and all seven Identity tables were preserved. Identity counts were 3 users, 7 roles, 3 user-role relationships and zero rows in the four other Identity tables. No migrations remain pending. Do not reset, recreate or access hms_dev.

## Verification

Run `dotnet build` and `dotnet test`. Tests use EF Core InMemory and temporary private files, never the development database. They cover role policies, authorization/antiforgery, validation, private upload/download, metadata/audit persistence, cleanup on database failure, patient ownership, profile access, archived record visibility, identity preservation and concurrency failures. PatientHttpTests uses the real MVC controller and compiled Razor views in an isolated TestHost. Its authentication scheme exists only in the test project; real Identity login/cookies still require browser verification.

The separate read-only database probe also verified PostgreSQL ILike search, archived listing, profile/edit loading and document mapping after migration. These queries run in a read-only transaction. The automated mutation tests do not exercise PostgreSQL transactions or filesystem crash recovery.

The migration is applied. Perform these final browser checks over HTTPS:

1. As Receptionist, register, search, open and edit a patient; verify emergency contacts and links.
2. Upload and download one real PDF, JPEG and PNG. Reject wrong signatures, mismatched MIME/extensions and files over 5 MiB. Confirm files cannot be fetched through a static URL or another patient's download route.
3. As Administrator, confirm archive, find the patient only in the archived list, download existing documents and reactivate with the same number/ID. Receptionist must be denied archive/reactivate/archived-list endpoints.
4. Open two edit forms, save one, and confirm the other refuses to overwrite it. Check missing/invalid antiforgery tokens reject POST requests and signed-out/other-role users cannot access patient endpoints.
5. Review audit records for registration, edit, upload/download, archive and reactivation.

No commit or push is performed by this work.

## Change inventory

Existing modified files at the start were PatientsController.cs, Views/Patients/Create.cshtml and Views/Shared/_Layout.cshtml. Existing untracked files were PatientDetailsViewModel.cs, PatientEditViewModel.cs, PatientListViewModel.cs and the patient Details/Edit/Index views. Their patient workflows were retained; the malformed registration view was repaired. The existing layout and PatientListViewModel were left untouched by this work.

Modified in this implementation:

- MediCareHMS.slnx: include the test project.
- src/MediCare.Web/Controllers/PatientsController.cs: patient audit, concurrency, archives and documents.
- src/MediCare.Web/Data/MediCareDbContext.cs: document mapping and concurrency token.
- src/MediCare.Web/Data/Migrations/MediCareDbContextModelSnapshot.cs: generated schema snapshot.
- src/MediCare.Web/Models/Patient.cs: concurrency token.
- src/MediCare.Web/Program.cs: private storage registration, database-name guard and protected development probe.
- src/MediCare.Web/ViewModels/PatientDetailsViewModel.cs and PatientEditViewModel.cs: documents/archive state and version fields.
- src/MediCare.Web/Views/Patients/Create.cshtml, Details.cshtml, Edit.cshtml and Index.cshtml: registration repair and patient UI integration.
- src/MediCare.Web/Views/SecurityAuditLogs/Index.cshtml: clarify patient/document record audit targets.

Created:

- .gitignore: exclude build output, private documents and test results.
- src/MediCare.Web/Models/PatientDocument.cs.
- src/MediCare.Web/Services/PatientDocumentStorage.cs.
- src/MediCare.Web/Data/Migrations/20261118171711_AddPatientDocumentsAndConcurrency.cs and its Designer.cs companion.
- src/MediCare.Web/Views/Patients/Archive.cshtml and Archived.cshtml.
- tests/MediCare.Web.Tests/MediCare.Web.Tests.csproj, PatientSecurityTests.cs and PatientHttpTests.cs.
- tools/MediCare.DatabaseCheck/MediCare.DatabaseCheck.csproj and Program.cs.
- tools/Step7-migration.sql.
- docs/STEP7.md.

Final verification: `dotnet build` passed with zero warnings and zero errors. `dotnet test --no-build --no-restore` passed all 31 cases (21 security/validation cases and 10 HTTP integration cases), with zero failures and zero skips. EF reports no model changes since the applied migration. Git whitespace checks passed. The test runner printed an informational fallback-to-temporary-artifacts message; it did not affect test results. No commit, push, merge or branch switch was performed; the branch remains feature/patient-list.

Backup verification, approved migration application, and Step 7F/7G implementation and automated verification are complete. Final acceptance still requires the browser checks above using real Identity accounts over HTTPS, especially representative real PDF/JPEG/PNG files and two browser tabs editing one patient. Production malware scanning remains a separate prerequisite. Database-backed mutation/concurrency integration tests and a database restore rehearsal have not been performed.
