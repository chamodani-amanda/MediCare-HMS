START TRANSACTION;
CREATE TABLE departments (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "NormalizedName" character varying(100) NOT NULL,
    "Description" character varying(1000),
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_departments" PRIMARY KEY ("Id")
);

CREATE TABLE doctors (
    "Id" uuid NOT NULL,
    "RegistrationNumber" character varying(50) NOT NULL,
    "FirstName" character varying(80) NOT NULL,
    "LastName" character varying(80) NOT NULL,
    "Qualifications" character varying(1000) NOT NULL,
    "Specialization" character varying(120) NOT NULL,
    "Email" character varying(150),
    "NormalizedEmail" character varying(150),
    "PhoneNumber" character varying(20),
    "DepartmentId" uuid NOT NULL,
    "IdentityUserId" character varying(450),
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "UpdatedAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_doctors" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_doctors_AspNetUsers_IdentityUserId" FOREIGN KEY ("IdentityUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_doctors_departments_DepartmentId" FOREIGN KEY ("DepartmentId") REFERENCES departments ("Id") ON DELETE RESTRICT
);

CREATE TABLE doctor_schedules (
    "Id" uuid NOT NULL,
    "DoctorId" uuid NOT NULL,
    "DayOfWeek" integer NOT NULL,
    "StartTime" time without time zone NOT NULL,
    "EndTime" time without time zone NOT NULL,
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_doctor_schedules" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_doctor_schedules_day" CHECK ("DayOfWeek" BETWEEN 0 AND 6),
    CONSTRAINT "CK_doctor_schedules_times" CHECK ("StartTime" < "EndTime"),
    CONSTRAINT "FK_doctor_schedules_doctors_DoctorId" FOREIGN KEY ("DoctorId") REFERENCES doctors ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_departments_NormalizedName" ON departments ("NormalizedName");

CREATE UNIQUE INDEX "IX_doctor_schedules_DoctorId_DayOfWeek_StartTime" ON doctor_schedules ("DoctorId", "DayOfWeek", "StartTime") WHERE "IsActive" = TRUE;

CREATE INDEX "IX_doctors_DepartmentId" ON doctors ("DepartmentId");

CREATE UNIQUE INDEX "IX_doctors_IdentityUserId" ON doctors ("IdentityUserId");

CREATE INDEX "IX_doctors_IsActive_LastName_FirstName" ON doctors ("IsActive", "LastName", "FirstName");

CREATE UNIQUE INDEX "IX_doctors_NormalizedEmail" ON doctors ("NormalizedEmail");

CREATE UNIQUE INDEX "IX_doctors_RegistrationNumber" ON doctors ("RegistrationNumber");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261118184537_AddDoctorManagement', '10.0.15');

COMMIT;

