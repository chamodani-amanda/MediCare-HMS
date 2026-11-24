# Step 14 — Billing and payments

## Approved scope and access

The user approved these development policies on 2026-11-19. No original hospital financial specification was found in this repository; replace these policies only through a separately reviewed change.

| Role | New permissions |
| --- | --- |
| Administrator | Service catalogue metadata, activation and prospective prices/history only. No patient charges, invoices, payments or receipts. |
| Accountant | Patient charges, draft/issued invoices, manual payment records, receipts, reversals, voids and financial summaries. |
| All other roles | No new financial access. Existing clinical permissions remain unchanged. |

An account explicitly assigned both roles can exercise both permissions. No accounts are seeded, promoted or assigned roles. Accountant projections expose patient number/name and administrative source references/dates only. Source validation joins may traverse clinical foreign keys but never return diagnoses, notes, prescriptions, sample details or results. Accountant links do not bypass the existing clinical controllers' authorization. Administrator financial access requires explicitly assigned Accountant membership.

All financial controllers require role authorization and no-store responses. Every mutation requires antiforgery validation. Server-side validation, safe conflict responses, GUID concurrency tokens and database uniqueness guard writes. Audit events contain actor, action and target identifier, not financial/clinical payloads. Application code does not log monetary values, bank references, clinical data or credentials; sensitive-data logging is not enabled. Deployment access logs and proxy/browser protections should be independently reviewed.

## Pricing and charges

- LKR only, decimal arithmetic. Unit prices and amounts use PostgreSQL numeric(14,2), with a supported maximum of LKR 999,999,999.99. Quantities use numeric(12,3), support 0.001–999,999, and line totals round to two places with MidpointRounding.AwayFromZero (PostgreSQL numeric round for nonnegative amounts).
- No actual prices, taxes, discounts, insurance contracts or clinical charge eligibility are invented or seeded. Administrator must configure an active service and an explicit effective price before charging. Categories can represent consultation, laboratory, pharmacy, inpatient, outpatient and other services.
- Service codes are uppercase, unique and permanent. Metadata/activation edits are audited and versioned. Price rows are immutable, unique per service/effective timestamp, and cannot be backdated; blank effective fields mean now. Future prices are supported. Latest effective price at submission is selected; the picker displays the currently effective price.
- Accountant manually selects the patient, service, positive quantity and optional source. Charges snapshot the configured price, service description, quantity and rounded total. Later catalogue changes do not rewrite charges or issued invoices. Zero configured prices and zero-total invoices are allowed; zero payments are not.
- Optional sources are appointments, laboratory requests, pharmacy dispensing entries, admissions or outpatient visits. Each reference must match the patient. These are administrative references only; status does not trigger or automatically authorize a clinical charge. No automatic charging, drug pricing inference, daily bed accrual or backdated service pricing is implemented.
- At most one source is allowed per charge. One service per source is enforced across price revisions, with service-row locking and a database guard. Manual charges without sources use explicit submission tokens to prevent accidental retries, not a blanket same-service/same-patient restriction.
- Charges are immutable. Wrong unbilled charges remain in history and may be excluded from drafts. A separate charge-cancellation/credit workflow is not implemented; no negative compensating charges are allowed. Source-linked charges cannot be independently regenerated, including after invoice voiding; reuse the original charge for a replacement invoice when appropriate.
- Charges support search/pagination. Source pickers show latest 100 administrative references per module for the selected patient; patient search returns up to 50 matches. Invoice selection displays up to 500 available charges.

## Invoices

- Draft → Issued; Issued → Voided only with a reason and no unreversed original payments. Drafts may be saved empty but need at least one line to issue. Patient and creation token are permanent. Draft editing adds/removes existing patient charges and recalculates totals; prices are changed only by configuring a new price and recording a new charge, never by editing historical lines.
- A charge may occur on only one live draft/issued invoice. Voided invoices retain their lines; their charges become available to a replacement invoice. Invoice totals must equal line totals at the deferred database boundary.
- Issuance freezes lines, totals and issue time. Payments only rotate the parent concurrency version/current actor; original issuance actor remains in the audit event. No invoice, line on an issued invoice, or payment history is silently deleted.
- Payment status is derived: Unpaid, PartiallyPaid or Paid for Issued invoices, otherwise Draft/Voided. Zero-total issued invoices are Paid. Outstanding is issued total minus net payments; voided/draft invoices have no payable balance.
- Unique PostgreSQL sequences generate MC-I-######## invoice and MC-R-######## receipt numbers. Drafts reserve invoice numbers. Gaps are expected, including after rolled-back provider tests; sequence increments are not transactional.
- UTC timestamps, current-time issuance/recording only, Asia/Colombo display. No backdating, reopening, credit notes, taxes, discounts, insurance, partial refunds, write-offs or refund processing. Correct issued invoices through full recorded payment reversals where appropriate, reasoned voiding and replacement issuance. Ledger reversal is not proof that cash/bank funds have been refunded.

## Payments and receipts

- Cash or manually recorded bank transfer only. This application does not process card/bank payments and stores no card numbers, CVVs or payment credentials. Bank references are trimmed/uppercased, required for BankTransfer, globally unique, and absent for Cash.
- Issued invoices accept multiple positive partial/full payments within their outstanding balance. Payment token uniqueness and payload comparisons make retries idempotent; a reused token with different information is rejected.
- PostgreSQL invoice-row locks, immutable ledger entries, deferred balance checks and optimistic parent versions coordinate competing payment/reversal/void requests. Negative payments, overpayments, reversing a reversal and repeated reversal of one payment are forbidden.
- A full reversal appends a separate positive-valued Reversal entry linked uniquely to the exact original payment and invoice, with actor/time and required reason. Net paid subtracts reversal entries. Original receipts remain accessible and display a reversed annotation.
- Dashboard shows current outstanding totals and date-filtered/paginated recorded payments/reversals. Invoice listing supports patient search and financial status filters, including outstanding invoices.
- Authorized invoice/receipt printing and PDF use the existing licensed font mechanism. Issued/voided invoice PDFs are supported; drafts use the clearly labelled browser print view. Complex scripts outside the existing PDF renderer's supported range are rejected with a browser-print alternative rather than silently corrupted. PDF downloads are audited before release. Browser printing supports deployment Unicode fonts.

## Database status and backup

Applied to **medicare_dev only**, after explicit approval: `20261119125827_AddBillingAndPayments`. Immediate preflight reconfirmed the target, sole pending migration, exact expected additive operations, matching SQL artifact and readable backup with unchanged checksum. No existing table was altered or backfilled. No migrations remain pending.

Six new tables: `billing_services`, `billing_prices`, `patient_charges`, `billing_invoices`, `billing_invoice_lines`, `billing_payments`. Two new numbering sequences and twelve financial triggers/functions enforce history, state, source and balance protections. All foreign keys restrict deletion. No existing table is altered or backfilled; no extensions, roles or credentials are installed. Role membership checks reuse the existing `medicare_stay_role(text,text)` helper. Automatic Down refuses to destroy financial history. Freeze `BillingDatabaseSql.cs` after application; future changes require a new migration.

Reviewed SQL artifact: `tools/Billing-migration.sql`. Read-only `--billing-preflight` checks the exact sole pending migration, additive operation types, table/sequence names, restrictive FKs, SQL artifact equivalence, absence of object collisions, required existing helper, target and backup. It fingerprints every existing table before/after and prints only counts and equality results, never row contents. Actual target confirmed **medicare_dev**; hms_dev is never connected.

Fresh non-overwriting PostgreSQL custom-format backup outside Git:

`D:\MediCare-Backups\medicare_dev_before_step7_20261119_130852_fcfadaa4.backup`

- Size: **177,915 bytes**.
- SHA-256: `86522D11A48FD96202B00DB2ED943B881F6E50355F3444AAE2BF997787E7CBFF`.
- `pg_restore --list` identified medicare_dev and table data; complete offline archive read to NUL passed. The reused backup helper retains its historical before_step7 filename prefix.
- Checksum and both archive checks were repeated immediately before approved application and passed. No restore rehearsal performed. Private patient-document files are outside this database-only archive.

The guarded `--apply-approved-billing` command performed preflight before applying exactly the approved target; `--verify-billing` and `--integration-billing` require the target to be applied with no pending migrations. PostgreSQL verification passed for all six tables/columns, restrictive foreign keys, validated checks, valid indexes, both bigint sequences/defaults, twelve enabled triggers and three initially deferred ledger checks. Rollback-isolated synthetic provider checks passed configured/effective pricing (future prices excluded), immutable price/charge history, decimal half-cent rounding, charge idempotency, draft creation/issuance, partial/full payments and outstanding balances, duplicate submission, overpayment/invalid actor rejection, stale invoice versions, deferred financial rollback, immutable payment/line/total guards, exact full reversals, invoice voiding and retained history. A synthetic LKR 100 invoice correctly showed net paid 40 and outstanding 60, then paid 100/outstanding zero, then net zero after full reversals. All fixtures, including Identity and audit entries, were rolled back. Full-row SHA-256 fingerprints matched for every original and new billing table. Number sequences advanced as expected despite rollback; all six billing tables remain empty. No synthetic hospital rows were committed.

## Verification and remaining checks

Post-application Release build: **0 warnings, 0 errors**. Complete regression suite rerun: **340 passed, 0 failed, 0 skipped** (all previous 276 plus 64 billing tests). EF reports no model changes since the applied migration. PostgreSQL preflight, backup verification, schema inspection and rollback-isolated financial mutation checks passed. Automated coverage includes money/quantity precision, rounding, source matching/duplicates for all five modules, invoice totals and immutable issuance rules, partial/full/invalid/duplicate payments, duplicate bank references, reversals, void/replacement, concurrency tokens, translated PostgreSQL projections, additive migration inspection, real HTTP role/CSRF/privacy checks, Razor rendering and invoice/receipt PDF downloads. EF InMemory tests do not prove database rollback or simultaneous PostgreSQL locking; the separate provider checks verified rollback and stale versions in a single transaction. Independent-session races remain outstanding.

Remaining provider checks: extend adversarial SQL coverage for concurrent source allocation and charge-to-invoice allocation; race independent sessions paying/reversing/voiding the same invoice and verify no overpayment or duplicate allocation. The successful stale-version test used independently tracked contexts sharing one rollback transaction, not independent simultaneous sessions. Existing committed synthetic patient/clinical/Identity rows were not fabricated solely to support those tests.

Browser acceptance: configure prices as Administrator; verify Accountant source/date/price choices, draft editing, issue/freeze, partial/full payments, duplicate double-clicks, stale forms, reversal reasons, void/replacement, outstanding/date filters, pagination, responsive layout, print headers/footers and PDF pagination/script handling. Verify separate/unlinked roles and multi-role accounts, session expiry, audit visibility, deployment TLS/log redaction and least-privilege database credentials. Shared database-owner credentials are trusted; these triggers are not per-user PostgreSQL row-level security.

## Files created/modified

- Added `Models/Billing.cs`, `ViewModels/BillingViewModels.cs`, `Services/BillingRules.cs`, `Services/BillingPdf.cs`.
- Added `Data/BillingMapping.cs`, `Data/BillingDatabaseSql.cs`, billing migration/designer and SQL artifact; updated DbContext and EF snapshot.
- Added `Controllers/BillingCatalogueController.cs`, `Controllers/BillingController.cs`, catalogue views and billing dashboard/charge/draft/invoice/payment/receipt views; updated shared navigation.
- Added `BillingTests.cs`, `BillingHttpTests.cs`, `BillingPersistenceTests.cs`.
- Added `tools/MediCare.DatabaseCheck/BillingDatabaseChecks.cs`, updated tool routes, and added this document.
- No Git operations, database reset, migration application, role seeding or changes to other module code/permissions were performed.
