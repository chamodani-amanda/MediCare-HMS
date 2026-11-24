# Pharmacy Management — Step 12

## Approved access and clinical decisions

- Pharmacists manage inventory and dispensing, using minimal patient name/number, exact prescription instructions and pharmacy history. They receive no EMR notes, diagnosis, patient contact details or unrestricted patient/profile access.
- Administrators manage only the medicine catalogue, including status and low-stock thresholds. No pharmacy stock, prescription, dispensing or clinical access is added to that role. Existing Doctor, Receptionist, Nurse, Laboratory Staff and Accountant permissions remain unchanged.
- Doctors explicitly select an exact active catalogue medicine (brand/name, generic name, strength, formulation and dispensing unit) and enter the authorized total quantity and exact unit on a new/amended prescription. The medication text must match the selected catalogue name. No dose interpretation, unit conversion, substitution or pharmacist inference is performed.
- Free-text historical prescriptions remain readable under existing EMR permissions but cannot be dispensed. A doctor must create an amendment with explicit authorization. Finalized history is never overwritten or backfilled with invented values.
- Stable prescription lineage survives amendment copies and draft edits. Remaining quantity equals the current authorized total minus all net dispensing in that lineage. Increasing the total or changing medicine requires explicit doctor confirmation. Changed medicine creates a distinct lineage. A source prescription must belong to the same encounter and author; duplicate lineage in one revision is rejected.
- Only prescriptions in the latest finalized encounter revision are eligible. Starting an amendment conservatively suspends the entire encounter's prescriptions until the amendment is finalized; removed orders cannot be dispensed. Archived patients, inactive/unlinked original doctors, cancelled/NoShow encounters and inactive medicines are excluded. Original doctor account link must still match the encounter author.
- No separate prescription-revocation button is introduced: removing an order through a finalized amendment supersedes it. Starting the draft immediately suspends old orders. Patient/archive, care-link and appointment eligibility changes also revoke dispensing eligibility.

## Inventory and dispensing

- Medicine codes and per-medicine batch numbers are trimmed/uppercased and unique. Medicine identity is permanent; register a distinct item for a different identity. Status and threshold changes are version checked and audited.
- Receiving creates a batch with zero available balance and an immutable positive receipt movement in one EF transaction. Additional verified receipts for an existing batch use a reasoned positive adjustment; this avoids duplicate batch metadata. Supplier and expiry are mandatory; manufacture date is optional. Batch identity and dates cannot be edited later.
- Quantities support three decimal places, strictly positive dispensing/receipts, with a maximum of 1,000,000,000 units. No automatic pack conversions. Negative adjustments are allowed with a reason but cannot make stock negative. Positive adjustments cannot introduce expired or inactive medicine stock.
- Expiry date itself is unavailable, using the Asia/Colombo calendar date. Near-expiry means the next 90 days. Low stock counts only unexpired available batches of active medicines and compares to the configured threshold.
- Default allocation is earliest expiry, then receiving time, then batch ID. A pharmacist may explicitly select a different matching unexpired batch only with a documented FEFO exception reason. The confirmation page shows the minimum patient/prescription details and eligible stock. Full and partial quantities share the same authorization balance.
- Unique submission tokens prevent a repeated receipt, adjustment, dispensing or reversal from writing twice. A simultaneous duplicate can return a safe conflict; retrying with the same token resolves to existing history.
- Full reasoned reversals require confirmation that all goods were physically returned to quarantine. They append a negative dispensing entry and matching positive **quarantine** ledger entries for the original batches. They restore authorization availability but **never increase saleable stock**. Partial reversals are not implemented; reverse the entire issue then explicitly reissue the correct quantity if appropriate. Physical quarantine storage, destruction and any later release require hospital procedures; no automated release or resale workflow is provided.
- Dispensing and movement history is append-only. There are no deletion endpoints. Administrative changes, stock changes, clinical pharmacy reads and dispensing/reversals are audited without copying clinical notes into audit logs. Billing is not implemented; stable dispensing/allocation IDs and quantities are available for a later module.

## PostgreSQL safety and concurrency

Migration: `20261119072825_AddPharmacyManagement` — **applied to medicare_dev after explicit user approval**.

Four new tables: `medicines`, `medicine_batches`, `inventory_movements`, `pharmacy_dispenses`. Four nullable columns are added to `medical_prescriptions`: medicine FK, lineage ID, authorized quantity and dispensing unit. Existing rows retain NULL values; existing clinical protection triggers remain installed. New FK deletion behavior is restrictive. No extension changes are required. Up contains no drops, resets, truncation, data replacement or clinical-data backfill. Down deliberately refuses automatic destructive rollback.

Reviewable SQL: `tools/PharmacyManagement-migration.sql`; guard definitions: `src/MediCare.Web/Data/PharmacyDatabaseSql.cs` (part of the immutable migration implementation; do not change after application).

Ten triggers protect medicine identity, batches, ledger and dispensing history, prescribing lineage, finalization, role/eligibility, allocation totals and FEFO. Encounter row locks serialize dispensing with amendment creation and finalization; batch row locks serialize stock changes. Database checks prohibit negative available stock; adjustments compare the submitted batch version inside the lock. Deferred allocation checks require every issue/reversal to balance its exact batch movements at transaction end. All effects and audit writes share the EF SaveChanges transaction. Deadlock/serialization/concurrency conflicts return safe reload messages; failed transactions do not issue stock.

The application identity is supplied to SQL by the trusted server and checked against Identity role membership. These checks are **not** a substitute for separating database credentials or PostgreSQL row-level security against a party with direct owner credentials. Owner-level clients can impersonate application actors; production database administration must remain restricted.

Fresh backup created and verified (outside Git):

`D:\MediCare-Backups\medicare_dev_before_step7_20261119_072445_ef948f41.backup`

- Size: 85,161 bytes.
- SHA-256: `A817E14B7EE0AE0BBBC3029B326901C13F10F10CBD201B7AC4BCEE21A469F798`.
- Custom archive listing and full offline pg_restore read to NUL passed; no database restore was performed. A restore rehearsal is outstanding. Private patient attachment files are outside this database backup and need their own backup procedure.
- Read-only preflight reconfirmed `medicare_dev`, sole pending pharmacy migration, matching SQL artifact, no object collisions, checksum/archive verification and unchanged full-row fingerprints for every existing table. `hms_dev` was not accessed.
- User approval was received, and target, migration structure, checksum and archive readability were reverified immediately before application. The approved migration then applied successfully. No unexpected destructive operations were found.

## Verification and remaining checks

Post-application verification: Release build passed with zero warnings and zero errors; all **229 automated tests passed**, including the previous 180 and 49 pharmacy tests. EF reports no model drift. All four pharmacy tables, primary keys, restrictive relationships, indexes, check constraints, four nullable prescription authorization fields, medicine FK and ten pharmacy triggers were verified. Both deferred allocation checks and all seven original EMR/laboratory guards remain enabled. All existing table row counts and full-row SHA-256 fingerprints were unchanged after migration and after fixture rollback.

Automated tests cover medicine validation/versioning, all role boundaries, CSRF, worklist privacy, legacy/draft/superseded eligibility, inactive/archived/revoked care cases, expiry dates and alerts, quantity precision, partial/full issuing, authorization caps, idempotency, receiving, reasoned quarantine reversal, amendment balances, EF concurrency, PostgreSQL query translation and additive migration structure.

In-memory HTTP tests do not execute PostgreSQL triggers. Do not claim they prove live stock deduction, uniqueness or simultaneous oversell protection.

The approved migration was applied through the guarded `--apply-approved-pharmacy` command, which rechecked the exact sole pending migration and verified the named backup before application. The pre-application `--pharmacy-preflight` command intentionally refuses after migration application. Use `--verify-pharmacy` for current read-only verification and `--integration-pharmacy` for rollback-isolated provider tests. The latter passed using temporary synthetic accounts, appointments, encounters, prescriptions and inventory; all fixtures and their audits were rolled back.

PostgreSQL checks passed: stock receiving and live balances, duplicate medicine codes/batches/submission tokens, insufficient-stock protection, valid and stale stock adjustment versions, full/partial dispensing, remaining authorization caps, idempotency, multi-batch FEFO, documented FEFO exceptions, rejection of expired receipt/dispensing, exact quarantine reversal without restoring saleable stock, append-only history, unauthorized actor rejection, draft suspension, amendment finalization and preserved net authorization balance, duplicate/changed-medicine/cross-encounter lineage rejection, superseded order exclusion and deferred-allocation rollback. Expected expiry and FEFO errors were checked against the specific guard message so unrelated constraints could not make those tests falsely pass.

Independent backend sessions must still race both the same prescription and different prescriptions sharing a batch and verify no oversell/over-authorization. The rollback-only single-session helper verifies stale snapshots/versions and atomic rejection, but does not prove simultaneous independent-session behavior. Existing committed Doctor, Appointment, EMR and Laboratory tables are empty; synthetic clinical fixtures were not committed merely to support cross-session testing. Existing baseline includes one patient, one patient-document row, three Identity users, seven roles, three user-role links and nine audit rows, all preserved.

The first integration run rejected a synthetic lowercase medicine code; the fixture was corrected to use the same uppercase convention as the application. Subsequent full provider runs passed. No application schema or clinical history was changed to bypass a guard.

Browser acceptance after migration/application restart: Administrator catalogue-only navigation and denied pharmacy URLs; Pharmacist inventory/worklist and denied EMR/patient URLs; Doctor exact prescribing/amendments and printable/PDF authorization visibility; mobile layout, search/paging, validation, FEFO exception choice, double-click/reload behavior, expiry/low-stock alerts, full/partial issues, quarantine reversal and audit screens. Confirm appropriate local accounts have explicit roles; do not seed or alter existing accounts automatically.

## Files changed for Step 12

- Added `Models/Pharmacy.cs`, `Data/PharmacyMapping.cs`, `Data/PharmacyDatabaseSql.cs`, `Services/PharmacyRules.cs`, `ViewModels/PharmacyViewModels.cs`, `Controllers/MedicinesController.cs`, `Controllers/PharmacyController.cs`.
- Added medicine catalogue/form and pharmacy worklist, batches, receiving/adjustment, history, dispensing, low-stock and shared navigation/pagination Razor views.
- Updated `Models/MedicalRecord.cs`, `ViewModels/MedicalRecordViewModels.cs`, `Controllers/MedicalRecordsController.cs`, `Services/MedicalRecordPdf.cs`, `Views/MedicalRecords/Form.cshtml`, `Views/MedicalRecords/_Report.cshtml`, `Views/Shared/_Layout.cshtml`, `Data/MediCareDbContext.cs`, `Controllers/ClinicalAdministrationControllerBase.cs`.
- Added pharmacy migration and designer, updated EF model snapshot, added SQL review artifact.
- Added `tests/MediCare.Web.Tests/PharmacyTests.cs`, `PharmacyPersistenceTests.cs`, `tools/MediCare.DatabaseCheck/PharmacyChecks.cs`; updated database-check routing in `Program.cs`.
- Added this document. No Git branch or remote operations performed.
