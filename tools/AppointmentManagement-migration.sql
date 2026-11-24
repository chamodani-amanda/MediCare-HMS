START TRANSACTION;
CREATE EXTENSION IF NOT EXISTS btree_gist;

CREATE TABLE appointments (
    "Id" uuid NOT NULL,
    "BookingToken" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "DoctorId" uuid NOT NULL,
    "StartAtUtc" timestamp with time zone NOT NULL,
    "EndAtUtc" timestamp with time zone NOT NULL,
    "DurationMinutes" integer NOT NULL,
    "Status" character varying(20) NOT NULL,
    "CancellationReason" character varying(500),
    "CancelledAtUtc" timestamp with time zone,
    "Version" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_appointments" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_appointments_booking_token" CHECK ("BookingToken" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "CK_appointments_cancellation" CHECK (("Status" = 'Cancelled' AND "CancelledAtUtc" IS NOT NULL) OR ("Status" <> 'Cancelled' AND "CancelledAtUtc" IS NULL AND "CancellationReason" IS NULL)),
    CONSTRAINT "CK_appointments_duration" CHECK (isfinite("StartAtUtc") AND isfinite("EndAtUtc") AND "EndAtUtc" > "StartAtUtc" AND "DurationMinutes" IN (15,30,45,60) AND "EndAtUtc" = "StartAtUtc" + "DurationMinutes" * INTERVAL '1 minute'),
    CONSTRAINT "CK_appointments_start_interval" CHECK (EXTRACT(MINUTE FROM ("StartAtUtc" AT TIME ZONE 'UTC'))::int % 15 = 0 AND EXTRACT(SECOND FROM ("StartAtUtc" AT TIME ZONE 'UTC')) = 0),
    CONSTRAINT "CK_appointments_status" CHECK ("Status" IN ('Scheduled','Completed','Cancelled','NoShow')),
    CONSTRAINT "FK_appointments_doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES doctors ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_appointments_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_appointments_BookingToken" ON appointments ("BookingToken");

CREATE INDEX "IX_appointments_DoctorId_StartAtUtc" ON appointments ("DoctorId", "StartAtUtc");

CREATE INDEX "IX_appointments_PatientId_StartAtUtc" ON appointments ("PatientId", "StartAtUtc");

CREATE INDEX "IX_appointments_StartAtUtc_Status" ON appointments ("StartAtUtc", "Status");

ALTER TABLE appointments
ADD CONSTRAINT "EX_appointments_doctor_time"
EXCLUDE USING gist
("DoctorId" WITH =, tstzrange("StartAtUtc", "EndAtUtc", '[)') WITH &&)
WHERE ("Status" <> 'Cancelled');

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119030236_AddAppointmentManagement', '10.0.15');

COMMIT;

