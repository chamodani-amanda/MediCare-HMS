START TRANSACTION;
ALTER TABLE medical_prescriptions ADD "AuthorizedQuantity" numeric(18,3);

ALTER TABLE medical_prescriptions ADD "DispensingUnit" character varying(60);

ALTER TABLE medical_prescriptions ADD "LineageId" uuid;

ALTER TABLE medical_prescriptions ADD "MedicineId" uuid;

CREATE TABLE medicines (
    "Id" uuid NOT NULL,
    "Code" character varying(40) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "GenericName" character varying(160) NOT NULL,
    "Strength" character varying(100) NOT NULL,
    "Formulation" character varying(100) NOT NULL,
    "DispensingUnit" character varying(60) NOT NULL,
    "LowStockThreshold" numeric(18,3) NOT NULL,
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_medicines" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_medicines_fields" CHECK (length(btrim("Code")) > 0 AND "Code" = upper(btrim("Code")) AND length(btrim("Name")) > 0 AND length(btrim("GenericName")) > 0 AND length(btrim("Strength")) > 0 AND length(btrim("Formulation")) > 0 AND length(btrim("DispensingUnit")) > 0),
    CONSTRAINT "CK_medicines_threshold" CHECK ("LowStockThreshold" BETWEEN 0 AND 1000000000)
);

CREATE TABLE pharmacy_dispenses (
    "Id" uuid NOT NULL,
    "Token" uuid NOT NULL,
    "PrescriptionId" uuid NOT NULL,
    "LineageId" uuid NOT NULL,
    "Quantity" numeric(18,3) NOT NULL,
    "ReversesId" uuid,
    "Reason" character varying(1000),
    "ActorUserId" character varying(450) NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_pharmacy_dispenses" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_pharmacy_dispenses_quantity" CHECK (("ReversesId" IS NULL AND "Quantity" > 0) OR ("ReversesId" IS NOT NULL AND "Quantity" < 0 AND length(btrim("Reason")) > 0 AND "Reason" IS NOT NULL)),
    CONSTRAINT "CK_pharmacy_dispenses_token" CHECK ("Token" <> '00000000-0000-0000-0000-000000000000'::uuid AND "LineageId" <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_pharmacy_dispenses_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_pharmacy_dispenses_medical_prescriptions_PrescriptionId" FOREIGN KEY ("PrescriptionId") REFERENCES medical_prescriptions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_pharmacy_dispenses_pharmacy_dispenses_ReversesId" FOREIGN KEY ("ReversesId") REFERENCES pharmacy_dispenses ("Id") ON DELETE RESTRICT
);

CREATE TABLE medicine_batches (
    "Id" uuid NOT NULL,
    "MedicineId" uuid NOT NULL,
    "BatchNumber" character varying(80) NOT NULL,
    "Supplier" character varying(200) NOT NULL,
    "ManufacturedOn" date,
    "ExpiresOn" date NOT NULL,
    "ReceivedAtUtc" timestamp with time zone NOT NULL,
    "AvailableQuantity" numeric(18,3) NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_medicine_batches" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_medicine_batches_balance" CHECK ("AvailableQuantity" BETWEEN 0 AND 1000000000),
    CONSTRAINT "CK_medicine_batches_fields" CHECK (length(btrim("BatchNumber")) > 0 AND "BatchNumber" = upper(btrim("BatchNumber")) AND length(btrim("Supplier")) > 0 AND ("ManufacturedOn" IS NULL OR "ManufacturedOn" <= "ExpiresOn")),
    CONSTRAINT "FK_medicine_batches_medicines_MedicineId" FOREIGN KEY ("MedicineId") REFERENCES medicines ("Id") ON DELETE RESTRICT
);

CREATE TABLE inventory_movements (
    "Id" uuid NOT NULL,
    "ExpectedBatchVersion" uuid,
    "Token" uuid NOT NULL,
    "BatchId" uuid NOT NULL,
    "DispenseId" uuid,
    "Kind" character varying(30) NOT NULL,
    "Quantity" numeric(18,3) NOT NULL,
    "Reason" character varying(1000) NOT NULL,
    "ActorUserId" character varying(450) NOT NULL,
    "OccurredAtUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_inventory_movements" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_inventory_movements_fields" CHECK ("Token" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim("Reason")) > 0 AND abs("Quantity") <= 1000000000),
    CONSTRAINT "CK_inventory_movements_kind" CHECK (("Kind" = 'Receive' AND "Quantity" > 0 AND "DispenseId" IS NULL) OR ("Kind" = 'Adjust' AND "Quantity" <> 0 AND "DispenseId" IS NULL) OR ("Kind" = 'Dispense' AND "Quantity" < 0 AND "DispenseId" IS NOT NULL) OR ("Kind" = 'QuarantineReturn' AND "Quantity" > 0 AND "DispenseId" IS NOT NULL)),
    CONSTRAINT "FK_inventory_movements_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_inventory_movements_medicine_batches_BatchId" FOREIGN KEY ("BatchId") REFERENCES medicine_batches ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_inventory_movements_pharmacy_dispenses_DispenseId" FOREIGN KEY ("DispenseId") REFERENCES pharmacy_dispenses ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_medical_prescriptions_LineageId" ON medical_prescriptions ("LineageId");

CREATE UNIQUE INDEX "IX_medical_prescriptions_MedicalRecordRevisionId_LineageId" ON medical_prescriptions ("MedicalRecordRevisionId", "LineageId") WHERE "LineageId" IS NOT NULL;

CREATE INDEX "IX_medical_prescriptions_MedicineId" ON medical_prescriptions ("MedicineId");

ALTER TABLE medical_prescriptions ADD CONSTRAINT "CK_medical_prescriptions_authorization" CHECK (("MedicineId" IS NULL AND "AuthorizedQuantity" IS NULL AND "DispensingUnit" IS NULL) OR ("MedicineId" IS NOT NULL AND "AuthorizedQuantity" > 0 AND "AuthorizedQuantity" <= 1000000000 AND "AuthorizedQuantity" IS NOT NULL AND length(btrim("DispensingUnit")) > 0 AND "DispensingUnit" IS NOT NULL AND "LineageId" IS NOT NULL AND "LineageId" <> '00000000-0000-0000-0000-000000000000'::uuid));

CREATE INDEX "IX_inventory_movements_ActorUserId" ON inventory_movements ("ActorUserId");

CREATE INDEX "IX_inventory_movements_BatchId_OccurredAtUtc" ON inventory_movements ("BatchId", "OccurredAtUtc");

CREATE INDEX "IX_inventory_movements_DispenseId" ON inventory_movements ("DispenseId");

CREATE UNIQUE INDEX "IX_inventory_movements_Token" ON inventory_movements ("Token");

CREATE INDEX "IX_medicine_batches_ExpiresOn" ON medicine_batches ("ExpiresOn");

CREATE UNIQUE INDEX "IX_medicine_batches_MedicineId_BatchNumber" ON medicine_batches ("MedicineId", "BatchNumber");

CREATE UNIQUE INDEX "IX_medicines_Code" ON medicines ("Code");

CREATE INDEX "IX_pharmacy_dispenses_ActorUserId" ON pharmacy_dispenses ("ActorUserId");

CREATE INDEX "IX_pharmacy_dispenses_LineageId" ON pharmacy_dispenses ("LineageId");

CREATE INDEX "IX_pharmacy_dispenses_PrescriptionId" ON pharmacy_dispenses ("PrescriptionId");

CREATE UNIQUE INDEX "IX_pharmacy_dispenses_ReversesId" ON pharmacy_dispenses ("ReversesId");

CREATE UNIQUE INDEX "IX_pharmacy_dispenses_Token" ON pharmacy_dispenses ("Token");

ALTER TABLE medical_prescriptions ADD CONSTRAINT "FK_medical_prescriptions_medicines_MedicineId" FOREIGN KEY ("MedicineId") REFERENCES medicines ("Id") ON DELETE RESTRICT;

CREATE FUNCTION public.medicare_pharmacy_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'Pharmacy history cannot be changed or deleted' USING ERRCODE='23514';
END $$;
CREATE TRIGGER medicare_movement_immutable BEFORE UPDATE OR DELETE ON public.inventory_movements FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_append_only();
CREATE TRIGGER medicare_dispense_immutable BEFORE UPDATE OR DELETE ON public.pharmacy_dispenses FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_append_only();

CREATE FUNCTION public.medicare_pharmacy_catalogue_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' OR (to_jsonb(NEW)-'Version'-'IsActive'-'LowStockThreshold') IS DISTINCT FROM (to_jsonb(OLD)-'Version'-'IsActive'-'LowStockThreshold') THEN
        RAISE EXCEPTION 'Medicine identity is permanent' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_medicine_guard BEFORE UPDATE OR DELETE ON public.medicines FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_catalogue_guard();

CREATE FUNCTION public.medicare_pharmacy_batch_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Batch history is permanent' USING ERRCODE='23514'; END IF;
    IF TG_OP='INSERT' THEN
        IF NEW."AvailableQuantity" <> 0 THEN RAISE EXCEPTION 'Stock requires a ledger entry' USING ERRCODE='23514'; END IF;
    ELSE
        IF (to_jsonb(NEW)-'Version'-'AvailableQuantity') IS DISTINCT FROM (to_jsonb(OLD)-'Version'-'AvailableQuantity') OR pg_trigger_depth() < 2 THEN
            RAISE EXCEPTION 'Batch changes require a ledger entry' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_batch_guard BEFORE INSERT OR UPDATE OR DELETE ON public.medicine_batches FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_batch_guard();

CREATE FUNCTION public.medicare_pharmacy_prescription_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE encounter uuid; unit text;
BEGIN
    IF NEW."MedicineId" IS NULL THEN RETURN NEW; END IF;
    SELECT "MedicalRecordId" INTO encounter FROM public.medical_record_revisions WHERE "Id"=NEW."MedicalRecordRevisionId";
    PERFORM 1 FROM public.medical_records WHERE "Id"=encounter FOR UPDATE;
    SELECT "DispensingUnit" INTO unit FROM public.medicines WHERE "Id"=NEW."MedicineId" AND "IsActive" FOR SHARE;
    IF NOT FOUND OR NEW."DispensingUnit" IS DISTINCT FROM unit THEN RAISE EXCEPTION 'Exact active medicine and dispensing unit are required' USING ERRCODE='23514'; END IF;
    IF EXISTS (SELECT 1 FROM public.medical_prescriptions p JOIN public.medical_record_revisions r ON r."Id"=p."MedicalRecordRevisionId" WHERE p."LineageId"=NEW."LineageId" AND (r."MedicalRecordId"<>encounter OR p."MedicineId" IS DISTINCT FROM NEW."MedicineId")) THEN
        RAISE EXCEPTION 'Prescription lineage cannot change patient or medicine' USING ERRCODE='23514';
    END IF;
    IF NEW."AuthorizedQuantity" < COALESCE((SELECT sum("Quantity") FROM public.pharmacy_dispenses WHERE "LineageId"=NEW."LineageId"),0) THEN
        RAISE EXCEPTION 'Authorization is below previously dispensed quantity' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_pharmacy_prescription_guard BEFORE INSERT OR UPDATE ON public.medical_prescriptions FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_prescription_guard();

CREATE FUNCTION public.medicare_pharmacy_finalize_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    PERFORM 1 FROM public.medical_records WHERE "Id"=NEW."MedicalRecordId" FOR UPDATE;
    IF NEW."FinalizedAtUtc" IS NOT NULL AND EXISTS (SELECT 1 FROM public.medical_prescriptions p WHERE p."MedicalRecordRevisionId"=NEW."Id" AND p."AuthorizedQuantity" < COALESCE((SELECT sum(d."Quantity") FROM public.pharmacy_dispenses d WHERE d."LineageId"=p."LineageId"),0)) THEN
        RAISE EXCEPTION 'Authorization is below previously dispensed quantity' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_pharmacy_finalize_guard BEFORE UPDATE ON public.medical_record_revisions FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_finalize_guard();

CREATE FUNCTION public.medicare_pharmacy_dispense_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE p public.medical_prescriptions; r public.medical_record_revisions; original public.pharmacy_dispenses; encounter uuid; used numeric;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" role ON role."Id"=ur."RoleId" WHERE ur."UserId"=NEW."ActorUserId" AND role."Name"='Pharmacist') THEN
        RAISE EXCEPTION 'Pharmacist role required' USING ERRCODE='23514';
    END IF;
    SELECT * INTO p FROM public.medical_prescriptions WHERE "Id"=NEW."PrescriptionId";
    SELECT "MedicalRecordId" INTO encounter FROM public.medical_record_revisions WHERE "Id"=p."MedicalRecordRevisionId";
    PERFORM 1 FROM public.medical_records WHERE "Id"=encounter FOR UPDATE;
    IF NOT FOUND OR p."LineageId" IS DISTINCT FROM NEW."LineageId" THEN RAISE EXCEPTION 'Invalid prescription lineage' USING ERRCODE='23514'; END IF;
    IF NEW."ReversesId" IS NOT NULL THEN
        SELECT * INTO original FROM public.pharmacy_dispenses WHERE "Id"=NEW."ReversesId";
        IF NOT FOUND OR original."ReversesId" IS NOT NULL OR original."PrescriptionId"<>NEW."PrescriptionId" OR original."LineageId"<>NEW."LineageId" OR NEW."Quantity"<>-original."Quantity" THEN
            RAISE EXCEPTION 'Invalid reversal' USING ERRCODE='23514';
        END IF;
        RETURN NEW;
    END IF;
    SELECT * INTO r FROM public.medical_record_revisions WHERE "Id"=p."MedicalRecordRevisionId";
    IF r."FinalizedAtUtc" IS NULL OR p."AuthorizedQuantity" IS NULL OR EXISTS (SELECT 1 FROM public.medical_record_revisions WHERE "MedicalRecordId"=encounter AND "Number">r."Number") THEN
        RAISE EXCEPTION 'Prescription is not the latest finalized order' USING ERRCODE='23514';
    END IF;
    PERFORM 1 FROM public.medical_records mr JOIN public.patients patient ON patient."Id"=mr."PatientId"
        JOIN public.doctors doctor ON doctor."Id"=mr."DoctorId" JOIN public.appointments a ON a."Id"=mr."AppointmentId"
        WHERE mr."Id"=encounter AND NOT patient."IsArchived" AND doctor."IsActive" AND doctor."IdentityUserId"=mr."AuthorUserId" AND a."Status" IN ('Scheduled','Completed')
        FOR UPDATE OF patient,doctor,a;
    IF NOT FOUND THEN RAISE EXCEPTION 'Prescription care eligibility has been revoked' USING ERRCODE='23514'; END IF;
    PERFORM 1 FROM public.medicines WHERE "Id"=p."MedicineId" AND "IsActive" AND "DispensingUnit"=p."DispensingUnit" FOR SHARE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Medicine unavailable' USING ERRCODE='23514'; END IF;
    SELECT COALESCE(sum("Quantity"),0) INTO used FROM public.pharmacy_dispenses WHERE "LineageId"=NEW."LineageId";
    IF used+NEW."Quantity">p."AuthorizedQuantity" THEN RAISE EXCEPTION 'Quantity exceeds remaining authorization' USING ERRCODE='23514'; END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_dispense_guard BEFORE INSERT ON public.pharmacy_dispenses FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_dispense_guard();

CREATE FUNCTION public.medicare_pharmacy_movement_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE batch public.medicine_batches; d public.pharmacy_dispenses; medicine uuid; today date;
BEGIN
    today := (clock_timestamp() AT TIME ZONE 'Asia/Colombo')::date;
    IF NOT EXISTS (SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" role ON role."Id"=ur."RoleId" WHERE ur."UserId"=NEW."ActorUserId" AND role."Name"='Pharmacist') THEN RAISE EXCEPTION 'Pharmacist role required' USING ERRCODE='23514'; END IF;
    SELECT * INTO batch FROM public.medicine_batches WHERE "Id"=NEW."BatchId" FOR UPDATE;
    IF NOT FOUND THEN RAISE EXCEPTION 'Invalid batch' USING ERRCODE='23514'; END IF;
    IF NEW."Kind"='Adjust' AND NEW."ExpectedBatchVersion" IS DISTINCT FROM batch."Version" THEN RAISE EXCEPTION 'Batch changed; reload adjustment' USING ERRCODE='23514'; END IF;
    IF NEW."Quantity">0 AND NEW."Kind"<>'QuarantineReturn' THEN
        PERFORM 1 FROM public.medicines WHERE "Id"=batch."MedicineId" AND "IsActive" FOR SHARE;
        IF NOT FOUND THEN RAISE EXCEPTION 'Medicine unavailable' USING ERRCODE='23514'; END IF;
    END IF;
    IF NEW."Kind" IN ('Receive','Adjust') AND NEW."Quantity">0 AND batch."ExpiresOn"<=today THEN RAISE EXCEPTION 'Expired stock unavailable' USING ERRCODE='23514'; END IF;
    IF NEW."DispenseId" IS NOT NULL THEN
        SELECT * INTO d FROM public.pharmacy_dispenses WHERE "Id"=NEW."DispenseId";
        SELECT "MedicineId" INTO medicine FROM public.medical_prescriptions WHERE "Id"=d."PrescriptionId";
        IF NOT FOUND OR medicine IS DISTINCT FROM batch."MedicineId" OR d."ActorUserId"<>NEW."ActorUserId" OR (d."ReversesId" IS NULL) <> (NEW."Kind"='Dispense') THEN
            RAISE EXCEPTION 'Dispensing allocation mismatch' USING ERRCODE='23514';
        END IF;
        IF NEW."Kind"='Dispense' AND batch."ExpiresOn"<=today THEN RAISE EXCEPTION 'Expired stock unavailable' USING ERRCODE='23514'; END IF;
    END IF;
    IF NEW."Kind"<>'QuarantineReturn' THEN
        UPDATE public.medicine_batches SET "AvailableQuantity"="AvailableQuantity"+NEW."Quantity", "Version"=gen_random_uuid() WHERE "Id"=NEW."BatchId";
    END IF;
    RETURN NEW;
END $$;
CREATE TRIGGER medicare_movement_guard BEFORE INSERT ON public.inventory_movements FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_movement_guard();

CREATE FUNCTION public.medicare_pharmacy_allocation_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE id uuid; d public.pharmacy_dispenses; today date;
BEGIN
    IF TG_TABLE_NAME='pharmacy_dispenses' THEN id := NEW."Id"; ELSE id := NEW."DispenseId"; END IF;
    IF id IS NULL THEN RETURN NULL; END IF;
    SELECT * INTO d FROM public.pharmacy_dispenses WHERE "Id"=id;
    IF abs(d."Quantity") <> COALESCE((SELECT sum(abs("Quantity")) FROM public.inventory_movements WHERE "DispenseId"=id),0) THEN
        RAISE EXCEPTION 'Dispensing and batch allocations must balance atomically' USING ERRCODE='23514';
    END IF;
    IF d."ReversesId" IS NOT NULL THEN
        IF EXISTS (SELECT "BatchId", sum("Quantity") FROM public.inventory_movements WHERE "DispenseId" IN (id,d."ReversesId") GROUP BY "BatchId" HAVING sum("Quantity")<>0) THEN
            RAISE EXCEPTION 'Reversal must quarantine the exact issued batches' USING ERRCODE='23514';
        END IF;
    ELSIF d."Reason" IS NULL THEN
        today := (clock_timestamp() AT TIME ZONE 'Asia/Colombo')::date;
        IF EXISTS (SELECT 1 FROM public.inventory_movements m JOIN public.medicine_batches issued ON issued."Id"=m."BatchId"
            JOIN public.medicine_batches earlier ON earlier."MedicineId"=issued."MedicineId" AND earlier."ExpiresOn">today AND earlier."AvailableQuantity">0
            AND (earlier."ExpiresOn",earlier."ReceivedAtUtc",earlier."Id") < (issued."ExpiresOn",issued."ReceivedAtUtc",issued."Id") WHERE m."DispenseId"=id) THEN
            RAISE EXCEPTION 'FEFO exception requires a documented reason' USING ERRCODE='23514';
        END IF;
    END IF;
    RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER medicare_dispense_balance AFTER INSERT ON public.pharmacy_dispenses DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_allocation_check();
CREATE CONSTRAINT TRIGGER medicare_movement_balance AFTER INSERT ON public.inventory_movements DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_pharmacy_allocation_check();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119072825_AddPharmacyManagement', '10.0.15');

COMMIT;

