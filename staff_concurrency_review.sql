START TRANSACTION;
CREATE UNIQUE INDEX "UX_attendances_one_open_per_employee"
ON public.attendances ("EmployeeId")
WHERE "CheckOutAtUtc" IS NULL;

CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE public.leave_requests
ADD CONSTRAINT "EX_leave_requests_no_active_overlap"
EXCLUDE USING gist (
    "EmployeeId" WITH =,
    daterange("StartDate", "EndDate", '[]') WITH &&
)
WHERE ("Status" IN ('Pending', 'Approved'));

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119190137_EnforceStaffConcurrency', '10.0.15');

COMMIT;

