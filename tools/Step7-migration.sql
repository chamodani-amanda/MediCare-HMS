START TRANSACTION;
ALTER TABLE patients ADD "Version" uuid NOT NULL DEFAULT '81c508c3-5d57-42ec-88b2-eab530cd7417';

CREATE TABLE patient_documents (
    "Id" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "OriginalFileName" character varying(255) NOT NULL,
    "StoredFileName" character varying(40) NOT NULL,
    "ContentType" character varying(50) NOT NULL,
    "Size" bigint NOT NULL,
    "UploadedByUserId" character varying(450) NOT NULL,
    "UploadedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_patient_documents" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_patient_documents_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_patient_documents_PatientId" ON patient_documents ("PatientId");

CREATE UNIQUE INDEX "IX_patient_documents_StoredFileName" ON patient_documents ("StoredFileName");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261118171711_AddPatientDocumentsAndConcurrency', '10.0.15');

COMMIT;

