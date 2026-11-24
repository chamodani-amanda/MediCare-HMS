namespace MediCare.Web.Data;

// Kept with the application so the generated migration can be reviewed independently of EF mappings.
internal static class PharmacyDatabaseSql
{
    public const string Guards = """
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
""";
}
