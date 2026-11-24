START TRANSACTION;
CREATE SEQUENCE billing_invoice_numbers START WITH 1 INCREMENT BY 1 NO CYCLE;

CREATE SEQUENCE billing_receipt_numbers START WITH 1 INCREMENT BY 1 NO CYCLE;

CREATE TABLE billing_invoices (
    "Id" uuid NOT NULL,
    "Number" bigint NOT NULL DEFAULT (nextval('billing_invoice_numbers')),
    "Token" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "State" character varying(20) NOT NULL,
    "Total" numeric(14,2) NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "IssuedAtUtc" timestamp with time zone,
    "VoidedAtUtc" timestamp with time zone,
    "VoidReason" character varying(1000),
    "ActorUserId" character varying(450) NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_billing_invoices" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_invoice_money" CHECK ("Total" BETWEEN 0 AND 999999999.99),
    CONSTRAINT "CK_invoice_state" CHECK (("State"='Draft' AND "IssuedAtUtc" IS NULL AND "VoidedAtUtc" IS NULL AND "VoidReason" IS NULL) OR ("State"='Issued' AND "IssuedAtUtc" IS NOT NULL AND "VoidedAtUtc" IS NULL AND "VoidReason" IS NULL) OR ("State"='Voided' AND "IssuedAtUtc" IS NOT NULL AND "VoidedAtUtc" IS NOT NULL AND length(btrim("VoidReason"))>0)),
    CONSTRAINT "CK_invoice_token" CHECK ("Token"<>'00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_billing_invoices_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_billing_invoices_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT
);

CREATE TABLE billing_services (
    "Id" uuid NOT NULL,
    "Code" character varying(40) NOT NULL,
    "Name" character varying(160) NOT NULL,
    "Category" character varying(80) NOT NULL,
    "Description" character varying(1000),
    "IsActive" boolean NOT NULL,
    "Version" uuid NOT NULL,
    CONSTRAINT "PK_billing_services" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_billing_service_code" CHECK (length(btrim("Code"))>0 AND "Code"=upper(btrim("Code")))
);

CREATE TABLE billing_payments (
    "Id" uuid NOT NULL,
    "Number" bigint NOT NULL DEFAULT (nextval('billing_receipt_numbers')),
    "Token" uuid NOT NULL,
    "InvoiceId" uuid NOT NULL,
    "Amount" numeric(14,2) NOT NULL,
    "Method" character varying(20) NOT NULL,
    "BankReference" character varying(100),
    "ReversesId" uuid,
    "Reason" character varying(1000),
    "RecordedAtUtc" timestamp with time zone NOT NULL,
    "ActorUserId" character varying(450) NOT NULL,
    CONSTRAINT "PK_billing_payments" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_payment_method" CHECK (("ReversesId" IS NOT NULL AND "Method"='Reversal' AND "BankReference" IS NULL AND length(btrim("Reason"))>0) OR ("ReversesId" IS NULL AND "Reason" IS NULL AND (("Method"='Cash' AND "BankReference" IS NULL) OR ("Method"='BankTransfer' AND length(btrim("BankReference"))>0)))),
    CONSTRAINT "CK_payment_money" CHECK ("Amount">0 AND "Amount"<=999999999.99),
    CONSTRAINT "CK_payment_token" CHECK ("Token"<>'00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_billing_payments_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_billing_payments_billing_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES billing_invoices ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_billing_payments_billing_payments_ReversesId" FOREIGN KEY ("ReversesId") REFERENCES billing_payments ("Id") ON DELETE RESTRICT
);

CREATE TABLE billing_prices (
    "Id" uuid NOT NULL,
    "ServiceId" uuid NOT NULL,
    "UnitPrice" numeric(14,2) NOT NULL,
    "EffectiveAtUtc" timestamp with time zone NOT NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ActorUserId" character varying(450) NOT NULL,
    CONSTRAINT "PK_billing_prices" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_billing_price_money" CHECK ("UnitPrice" BETWEEN 0 AND 999999999.99),
    CONSTRAINT "CK_billing_price_time" CHECK (isfinite("EffectiveAtUtc") AND isfinite("CreatedAtUtc")),
    CONSTRAINT "FK_billing_prices_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_billing_prices_billing_services_ServiceId" FOREIGN KEY ("ServiceId") REFERENCES billing_services ("Id") ON DELETE RESTRICT
);

CREATE TABLE patient_charges (
    "Id" uuid NOT NULL,
    "Token" uuid NOT NULL,
    "PatientId" uuid NOT NULL,
    "PriceId" uuid NOT NULL,
    "Description" character varying(240) NOT NULL,
    "Quantity" numeric(12,3) NOT NULL,
    "UnitPrice" numeric(14,2) NOT NULL,
    "Total" numeric(14,2) NOT NULL,
    "AppointmentId" uuid,
    "LabRequestId" uuid,
    "PharmacyDispenseId" uuid,
    "AdmissionId" uuid,
    "OutpatientVisitId" uuid,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "ActorUserId" character varying(450) NOT NULL,
    CONSTRAINT "PK_patient_charges" PRIMARY KEY ("Id"),
    CONSTRAINT "CK_charge_money" CHECK ("Quantity">0 AND "Quantity"<=999999 AND "UnitPrice" BETWEEN 0 AND 999999999.99 AND "Total"=round("Quantity"*"UnitPrice",2) AND "Total"<=999999999.99),
    CONSTRAINT "CK_charge_source" CHECK (num_nonnulls("AppointmentId","LabRequestId","PharmacyDispenseId","AdmissionId","OutpatientVisitId")<=1),
    CONSTRAINT "CK_charge_token" CHECK ("Token"<>'00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_patient_charges_AspNetUsers_ActorUserId" FOREIGN KEY ("ActorUserId") REFERENCES "AspNetUsers" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_admissions_AdmissionId" FOREIGN KEY ("AdmissionId") REFERENCES admissions ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_appointments_AppointmentId" FOREIGN KEY ("AppointmentId") REFERENCES appointments ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_billing_prices_PriceId" FOREIGN KEY ("PriceId") REFERENCES billing_prices ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_lab_requests_LabRequestId" FOREIGN KEY ("LabRequestId") REFERENCES lab_requests ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_outpatient_visits_OutpatientVisitId" FOREIGN KEY ("OutpatientVisitId") REFERENCES outpatient_visits ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES patients ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_patient_charges_pharmacy_dispenses_PharmacyDispenseId" FOREIGN KEY ("PharmacyDispenseId") REFERENCES pharmacy_dispenses ("Id") ON DELETE RESTRICT
);

CREATE TABLE billing_invoice_lines (
    "Id" uuid NOT NULL,
    "InvoiceId" uuid NOT NULL,
    "ChargeId" uuid NOT NULL,
    CONSTRAINT "PK_billing_invoice_lines" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_billing_invoice_lines_billing_invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES billing_invoices ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_billing_invoice_lines_patient_charges_ChargeId" FOREIGN KEY ("ChargeId") REFERENCES patient_charges ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_billing_invoice_lines_ChargeId" ON billing_invoice_lines ("ChargeId");

CREATE UNIQUE INDEX "IX_billing_invoice_lines_InvoiceId_ChargeId" ON billing_invoice_lines ("InvoiceId", "ChargeId");

CREATE INDEX "IX_billing_invoices_ActorUserId" ON billing_invoices ("ActorUserId");

CREATE UNIQUE INDEX "IX_billing_invoices_Number" ON billing_invoices ("Number");

CREATE INDEX "IX_billing_invoices_PatientId" ON billing_invoices ("PatientId");

CREATE INDEX "IX_billing_invoices_State_CreatedAtUtc" ON billing_invoices ("State", "CreatedAtUtc");

CREATE UNIQUE INDEX "IX_billing_invoices_Token" ON billing_invoices ("Token");

CREATE INDEX "IX_billing_payments_ActorUserId" ON billing_payments ("ActorUserId");

CREATE UNIQUE INDEX "IX_billing_payments_BankReference" ON billing_payments ("BankReference");

CREATE INDEX "IX_billing_payments_InvoiceId" ON billing_payments ("InvoiceId");

CREATE UNIQUE INDEX "IX_billing_payments_Number" ON billing_payments ("Number");

CREATE INDEX "IX_billing_payments_RecordedAtUtc" ON billing_payments ("RecordedAtUtc");

CREATE UNIQUE INDEX "IX_billing_payments_ReversesId" ON billing_payments ("ReversesId");

CREATE UNIQUE INDEX "IX_billing_payments_Token" ON billing_payments ("Token");

CREATE INDEX "IX_billing_prices_ActorUserId" ON billing_prices ("ActorUserId");

CREATE UNIQUE INDEX "IX_billing_prices_ServiceId_EffectiveAtUtc" ON billing_prices ("ServiceId", "EffectiveAtUtc");

CREATE INDEX "IX_billing_services_Category_IsActive" ON billing_services ("Category", "IsActive");

CREATE UNIQUE INDEX "IX_billing_services_Code" ON billing_services ("Code");

CREATE INDEX "IX_patient_charges_ActorUserId" ON patient_charges ("ActorUserId");

CREATE INDEX "IX_patient_charges_AdmissionId" ON patient_charges ("AdmissionId");

CREATE INDEX "IX_patient_charges_AppointmentId" ON patient_charges ("AppointmentId");

CREATE INDEX "IX_patient_charges_LabRequestId" ON patient_charges ("LabRequestId");

CREATE INDEX "IX_patient_charges_OutpatientVisitId" ON patient_charges ("OutpatientVisitId");

CREATE INDEX "IX_patient_charges_PatientId_CreatedAtUtc" ON patient_charges ("PatientId", "CreatedAtUtc");

CREATE INDEX "IX_patient_charges_PharmacyDispenseId" ON patient_charges ("PharmacyDispenseId");

CREATE INDEX "IX_patient_charges_PriceId" ON patient_charges ("PriceId");

CREATE UNIQUE INDEX "IX_patient_charges_Token" ON patient_charges ("Token");

CREATE FUNCTION public.medicare_billing_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Financial history is immutable' USING ERRCODE='23514'; END $$;
CREATE TRIGGER billing_price_immutable BEFORE UPDATE OR DELETE ON public.billing_prices FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_immutable();
CREATE TRIGGER billing_charge_immutable BEFORE UPDATE OR DELETE ON public.patient_charges FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_immutable();
CREATE TRIGGER billing_payment_immutable BEFORE UPDATE OR DELETE ON public.billing_payments FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_immutable();

CREATE FUNCTION public.medicare_billing_catalogue_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='billing_prices' THEN
  IF NOT public.medicare_stay_role(NEW."ActorUserId",'Administrator') OR NEW."EffectiveAtUtc"<NEW."CreatedAtUtc" THEN RAISE EXCEPTION 'Authorized prospective pricing required' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM public.billing_services WHERE "Id"=NEW."ServiceId" AND "IsActive" FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'Active service required' USING ERRCODE='23514'; END IF;
 ELSE
  IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Service history is permanent' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND NEW."Code"<>OLD."Code" THEN RAISE EXCEPTION 'Service code is permanent' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_catalogue_guard BEFORE UPDATE OR DELETE ON public.billing_services FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_catalogue_guard();
CREATE TRIGGER billing_price_guard BEFORE INSERT ON public.billing_prices FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_catalogue_guard();

CREATE FUNCTION public.medicare_billing_charge_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE p public.billing_prices; service_id uuid; source_patient uuid;
BEGIN
 IF NOT public.medicare_stay_role(NEW."ActorUserId",'Accountant') THEN RAISE EXCEPTION 'Accountant required' USING ERRCODE='23514'; END IF;
 IF NOT isfinite(NEW."CreatedAtUtc") THEN RAISE EXCEPTION 'Finite charge time required' USING ERRCODE='23514'; END IF;
 SELECT * INTO p FROM public.billing_prices WHERE "Id"=NEW."PriceId";service_id:=p."ServiceId";
 PERFORM 1 FROM public.billing_services WHERE "Id"=service_id AND "IsActive" FOR UPDATE;
 IF NOT FOUND OR p."UnitPrice"<>NEW."UnitPrice" OR p."EffectiveAtUtc">NEW."CreatedAtUtc" OR EXISTS(SELECT 1 FROM public.billing_prices WHERE "ServiceId"=service_id AND "EffectiveAtUtc">p."EffectiveAtUtc" AND "EffectiveAtUtc"<=NEW."CreatedAtUtc") THEN RAISE EXCEPTION 'Applicable configured price required' USING ERRCODE='23514'; END IF;
 IF NEW."AppointmentId" IS NOT NULL THEN SELECT "PatientId" INTO source_patient FROM public.appointments WHERE "Id"=NEW."AppointmentId";
 ELSIF NEW."LabRequestId" IS NOT NULL THEN SELECT mr."PatientId" INTO source_patient FROM public.lab_requests lr JOIN public.medical_records mr ON mr."Id"=lr."MedicalRecordId" WHERE lr."Id"=NEW."LabRequestId";
 ELSIF NEW."PharmacyDispenseId" IS NOT NULL THEN SELECT mr."PatientId" INTO source_patient FROM public.pharmacy_dispenses pd JOIN public.medical_prescriptions rx ON rx."Id"=pd."PrescriptionId" JOIN public.medical_record_revisions rev ON rev."Id"=rx."MedicalRecordRevisionId" JOIN public.medical_records mr ON mr."Id"=rev."MedicalRecordId" WHERE pd."Id"=NEW."PharmacyDispenseId";
 ELSIF NEW."AdmissionId" IS NOT NULL THEN SELECT "PatientId" INTO source_patient FROM public.admissions WHERE "Id"=NEW."AdmissionId";
 ELSIF NEW."OutpatientVisitId" IS NOT NULL THEN SELECT "PatientId" INTO source_patient FROM public.outpatient_visits WHERE "Id"=NEW."OutpatientVisitId";
 ELSE source_patient:=NEW."PatientId"; END IF;
 IF source_patient IS DISTINCT FROM NEW."PatientId" THEN RAISE EXCEPTION 'Source patient mismatch' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.patient_charges c JOIN public.billing_prices bp ON bp."Id"=c."PriceId" WHERE bp."ServiceId"=service_id AND ((NEW."AppointmentId" IS NOT NULL AND c."AppointmentId"=NEW."AppointmentId") OR (NEW."LabRequestId" IS NOT NULL AND c."LabRequestId"=NEW."LabRequestId") OR (NEW."PharmacyDispenseId" IS NOT NULL AND c."PharmacyDispenseId"=NEW."PharmacyDispenseId") OR (NEW."AdmissionId" IS NOT NULL AND c."AdmissionId"=NEW."AdmissionId") OR (NEW."OutpatientVisitId" IS NOT NULL AND c."OutpatientVisitId"=NEW."OutpatientVisitId"))) THEN RAISE EXCEPTION 'Source already charged for service' USING ERRCODE='23505'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_charge_guard BEFORE INSERT ON public.patient_charges FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_charge_guard();

CREATE FUNCTION public.medicare_billing_invoice_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Invoice history is permanent' USING ERRCODE='23514'; END IF;
 IF NOT public.medicare_stay_role(NEW."ActorUserId",'Accountant') THEN RAISE EXCEPTION 'Accountant required' USING ERRCODE='23514'; END IF;
 IF NOT isfinite(NEW."CreatedAtUtc") OR (NEW."IssuedAtUtc" IS NOT NULL AND (NOT isfinite(NEW."IssuedAtUtc") OR NEW."IssuedAtUtc"<NEW."CreatedAtUtc")) OR (NEW."VoidedAtUtc" IS NOT NULL AND (NOT isfinite(NEW."VoidedAtUtc") OR NEW."VoidedAtUtc"<NEW."IssuedAtUtc")) OR (NEW."State"='Voided' AND (NEW."VoidReason" IS NULL OR length(btrim(NEW."VoidReason"))=0)) THEN RAISE EXCEPTION 'Invalid invoice time or void reason' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW."State"<>'Draft' THEN RAISE EXCEPTION 'Create draft first' USING ERRCODE='23514'; END IF;
 ELSE
  IF (to_jsonb(NEW)-'State'-'Total'-'IssuedAtUtc'-'VoidedAtUtc'-'VoidReason'-'Version'-'ActorUserId') IS DISTINCT FROM (to_jsonb(OLD)-'State'-'Total'-'IssuedAtUtc'-'VoidedAtUtc'-'VoidReason'-'Version'-'ActorUserId') OR OLD."State"='Voided' THEN RAISE EXCEPTION 'Invoice identity is permanent' USING ERRCODE='23514'; END IF;
  IF OLD."State"='Issued' AND ((to_jsonb(NEW)-'State'-'VoidedAtUtc'-'VoidReason'-'Version'-'ActorUserId') IS DISTINCT FROM (to_jsonb(OLD)-'State'-'VoidedAtUtc'-'VoidReason'-'Version'-'ActorUserId') OR NEW."State" NOT IN ('Issued','Voided')) THEN RAISE EXCEPTION 'Issued invoice is immutable' USING ERRCODE='23514'; END IF;
  IF OLD."State"='Draft' AND NEW."State" NOT IN ('Draft','Issued') THEN RAISE EXCEPTION 'Invalid invoice transition' USING ERRCODE='23514'; END IF;
  IF OLD."State"='Draft' AND NEW."State"='Issued' AND NOT EXISTS(SELECT 1 FROM public.billing_invoice_lines WHERE "InvoiceId"=NEW."Id") THEN RAISE EXCEPTION 'Invoice lines required' USING ERRCODE='23514'; END IF;
  IF NEW."State"='Voided' AND EXISTS(SELECT 1 FROM public.billing_payments p WHERE p."InvoiceId"=NEW."Id" AND p."ReversesId" IS NULL AND NOT EXISTS(SELECT 1 FROM public.billing_payments r WHERE r."ReversesId"=p."Id")) THEN RAISE EXCEPTION 'Reverse payments before voiding' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_invoice_guard BEFORE INSERT OR UPDATE OR DELETE ON public.billing_invoices FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_invoice_guard();

CREATE FUNCTION public.medicare_billing_line_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE invoice_id uuid; state text; patient uuid; charge_patient uuid;
BEGIN
 IF TG_OP='UPDATE' THEN RAISE EXCEPTION 'Replace draft line instead' USING ERRCODE='23514'; END IF;
 IF TG_OP='DELETE' THEN invoice_id:=OLD."InvoiceId"; ELSE invoice_id:=NEW."InvoiceId"; END IF;
 SELECT "State","PatientId" INTO state,patient FROM public.billing_invoices WHERE "Id"=invoice_id FOR UPDATE;
 IF state IS DISTINCT FROM 'Draft' THEN RAISE EXCEPTION 'Draft invoice required' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  SELECT "PatientId" INTO charge_patient FROM public.patient_charges WHERE "Id"=NEW."ChargeId" FOR UPDATE;
  IF charge_patient IS DISTINCT FROM patient OR EXISTS(SELECT 1 FROM public.billing_invoice_lines l JOIN public.billing_invoices i ON i."Id"=l."InvoiceId" WHERE l."ChargeId"=NEW."ChargeId" AND i."State"<>'Voided') THEN RAISE EXCEPTION 'Charge unavailable for invoice' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 RETURN OLD;
END $$;
CREATE TRIGGER billing_line_guard BEFORE INSERT OR UPDATE OR DELETE ON public.billing_invoice_lines FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_line_guard();

CREATE FUNCTION public.medicare_billing_payment_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE i public.billing_invoices; original public.billing_payments; paid numeric;
BEGIN
 SELECT * INTO i FROM public.billing_invoices WHERE "Id"=NEW."InvoiceId" FOR UPDATE;
 IF i."State" IS DISTINCT FROM 'Issued' OR NOT public.medicare_stay_role(NEW."ActorUserId",'Accountant') THEN RAISE EXCEPTION 'Accountant and issued invoice required' USING ERRCODE='23514'; END IF;
 IF NOT isfinite(NEW."RecordedAtUtc") OR NEW."RecordedAtUtc"<i."IssuedAtUtc" OR (NEW."Method"='BankTransfer' AND (NEW."BankReference" IS NULL OR length(btrim(NEW."BankReference"))=0 OR NEW."BankReference"<>upper(btrim(NEW."BankReference")))) OR (NEW."ReversesId" IS NOT NULL AND (NEW."Reason" IS NULL OR length(btrim(NEW."Reason"))=0)) THEN RAISE EXCEPTION 'Valid payment time and reference required' USING ERRCODE='23514'; END IF;
 IF NEW."ReversesId" IS NOT NULL THEN
  SELECT * INTO original FROM public.billing_payments WHERE "Id"=NEW."ReversesId";
  IF NOT FOUND OR original."ReversesId" IS NOT NULL OR original."InvoiceId"<>NEW."InvoiceId" OR original."Amount"<>NEW."Amount" OR NEW."RecordedAtUtc"<original."RecordedAtUtc" THEN RAISE EXCEPTION 'Exact original payment reversal required' USING ERRCODE='23514'; END IF;
 ELSE
  SELECT COALESCE(sum(CASE WHEN "ReversesId" IS NULL THEN "Amount" ELSE -"Amount" END),0) INTO paid FROM public.billing_payments WHERE "InvoiceId"=i."Id";
  IF NEW."Amount">i."Total"-paid THEN RAISE EXCEPTION 'Payment exceeds balance' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER billing_payment_guard BEFORE INSERT ON public.billing_payments FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_payment_guard();

CREATE FUNCTION public.medicare_billing_balance_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE invoice_id uuid; i public.billing_invoices; total numeric; paid numeric;
BEGIN
 IF TG_TABLE_NAME='billing_invoices' THEN invoice_id:=NEW."Id"; ELSIF TG_OP='DELETE' THEN invoice_id:=OLD."InvoiceId"; ELSE invoice_id:=NEW."InvoiceId"; END IF;
 SELECT * INTO i FROM public.billing_invoices WHERE "Id"=invoice_id;
 SELECT COALESCE(sum(c."Total"),0) INTO total FROM public.billing_invoice_lines l JOIN public.patient_charges c ON c."Id"=l."ChargeId" WHERE l."InvoiceId"=invoice_id;
 SELECT COALESCE(sum(CASE WHEN "ReversesId" IS NULL THEN "Amount" ELSE -"Amount" END),0) INTO paid FROM public.billing_payments WHERE "InvoiceId"=invoice_id;
 IF i."Total"<>total OR paid<0 OR paid>total OR (i."State"<>'Issued' AND paid<>0) THEN RAISE EXCEPTION 'Invoice ledger must balance' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER billing_invoice_balance AFTER INSERT OR UPDATE ON public.billing_invoices DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_balance_check();
CREATE CONSTRAINT TRIGGER billing_line_balance AFTER INSERT OR UPDATE OR DELETE ON public.billing_invoice_lines DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_balance_check();
CREATE CONSTRAINT TRIGGER billing_payment_balance AFTER INSERT ON public.billing_payments DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_billing_balance_check();

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119125827_AddBillingAndPayments', '10.0.15');

COMMIT;

