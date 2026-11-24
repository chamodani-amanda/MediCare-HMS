namespace MediCare.Web.Data;

internal static class HospitalStayDatabaseSql
{
    // Migration implementation: freeze this source after application.
    public const string Guards = """
ALTER TABLE public.bed_occupancies ADD CONSTRAINT "EX_bed_occupancies_time"
EXCLUDE USING gist ("BedId" WITH =, tstzrange("StartedAtUtc","EndedAtUtc",'[)') WITH &&);

CREATE FUNCTION public.medicare_stay_role(account text, role_name text) RETURNS boolean LANGUAGE sql STABLE AS $$
 SELECT EXISTS(SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" r ON r."Id"=ur."RoleId" WHERE ur."UserId"=account AND r."Name"=role_name)
$$;

CREATE FUNCTION public.medicare_stay_no_delete() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Hospital stay history is permanent' USING ERRCODE='23514'; END $$;
CREATE TRIGGER medicare_transfers_immutable BEFORE UPDATE OR DELETE ON public.bed_transfers FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_no_delete();

CREATE FUNCTION public.medicare_stay_facility_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Facility history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='wards' THEN
  IF NEW."Code"<>OLD."Code" OR NEW."DepartmentId"<>OLD."DepartmentId" THEN RAISE EXCEPTION 'Ward identity is permanent' USING ERRCODE='23514'; END IF;
  IF NOT NEW."IsActive" AND EXISTS(SELECT 1 FROM public.bed_occupancies o JOIN public.beds b ON b."Id"=o."BedId" WHERE b."WardId"=NEW."Id" AND o."EndedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Occupied ward cannot be deactivated' USING ERRCODE='23514'; END IF;
 ELSE
  IF NEW."Number"<>OLD."Number" OR NEW."WardId"<>OLD."WardId" THEN RAISE EXCEPTION 'Bed identity is permanent' USING ERRCODE='23514'; END IF;
  IF NOT NEW."IsOperational" AND EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "BedId"=NEW."Id" AND "EndedAtUtc" IS NULL) THEN RAISE EXCEPTION 'Occupied bed cannot be deactivated' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_ward_guard BEFORE UPDATE OR DELETE ON public.wards FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_facility_guard();
CREATE TRIGGER medicare_bed_guard BEFORE UPDATE OR DELETE ON public.beds FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_facility_guard();

CREATE FUNCTION public.medicare_admission_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE owner_account text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Admission history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW."Status"<>'Admitted' OR NOT public.medicare_stay_role(NEW."AdmittedByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist admission registration required' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM public.patients p JOIN public.doctors d ON d."Id"=NEW."DoctorId" JOIN public.departments dept ON dept."Id"=d."DepartmentId"
   WHERE p."Id"=NEW."PatientId" AND NOT p."IsArchived" AND d."IsActive" AND dept."IsActive" AND d."DepartmentId"=NEW."DepartmentId" AND d."IdentityUserId" IS NOT NULL
   FOR UPDATE OF p,d,dept;
  IF NOT FOUND THEN RAISE EXCEPTION 'Admission patient or doctor is ineligible' USING ERRCODE='23514'; END IF;
 ELSE
  IF (to_jsonb(NEW)-'Status'-'Version'-'ClearedAtUtc'-'ClearedByUserId'-'DischargedAtUtc'-'DischargedByUserId') IS DISTINCT FROM (to_jsonb(OLD)-'Status'-'Version'-'ClearedAtUtc'-'ClearedByUserId'-'DischargedAtUtc'-'DischargedByUserId') THEN RAISE EXCEPTION 'Admission identity is permanent' USING ERRCODE='23514'; END IF;
  IF OLD."Status"='Discharged' THEN RAISE EXCEPTION 'Discharged admission is immutable' USING ERRCODE='23514'; END IF;
  IF OLD."Status"=NEW."Status" THEN
   IF (to_jsonb(NEW)-'Version') IS DISTINCT FROM (to_jsonb(OLD)-'Version') THEN RAISE EXCEPTION 'Invalid admission transition' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='Admitted' AND NEW."Status"='DischargeReady' THEN
   SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=NEW."DoctorId" AND "IsActive" FOR UPDATE;
   IF owner_account IS NULL OR owner_account IS DISTINCT FROM NEW."ClearedByUserId" OR NOT public.medicare_stay_role(owner_account,'Doctor') OR NOT EXISTS(SELECT 1 FROM public.admission_clinical_notes WHERE "AdmissionId"=NEW."Id" AND "AuthorUserId"=owner_account AND length(btrim("AdmissionReason"))>0 AND length(btrim("DischargeSummary"))>0) THEN RAISE EXCEPTION 'Treating doctor clinical discharge clearance required' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='DischargeReady' AND NEW."Status"='Discharged' THEN
   IF NEW."ClearedAtUtc" IS DISTINCT FROM OLD."ClearedAtUtc" OR NEW."ClearedByUserId" IS DISTINCT FROM OLD."ClearedByUserId" OR NOT public.medicare_stay_role(NEW."DischargedByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist checkout after preserved clinical clearance required' USING ERRCODE='23514'; END IF;
  ELSE RAISE EXCEPTION 'Invalid admission transition' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_admission_guard BEFORE INSERT OR UPDATE OR DELETE ON public.admissions FOR EACH ROW EXECUTE FUNCTION public.medicare_admission_guard();

CREATE FUNCTION public.medicare_occupancy_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE a public.admissions; department uuid;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Occupancy history is permanent' USING ERRCODE='23514'; END IF;
 SELECT * INTO a FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Admission required' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF a."Status"<>'Admitted' OR NEW."EndedAtUtc" IS NOT NULL OR NEW."StartedAtUtc"<a."AdmittedAtUtc" THEN RAISE EXCEPTION 'Only admitted patients can receive beds' USING ERRCODE='23514'; END IF;
  SELECT w."DepartmentId" INTO department FROM public.beds b JOIN public.wards w ON w."Id"=b."WardId" JOIN public.departments d ON d."Id"=w."DepartmentId" WHERE b."Id"=NEW."BedId" AND b."IsOperational" AND w."IsActive" AND d."IsActive" FOR UPDATE OF b,w,d;
  IF NOT FOUND OR department<>a."DepartmentId" THEN RAISE EXCEPTION 'Operational available bed in treating department required' USING ERRCODE='23514'; END IF;
 ELSE
  IF OLD."EndedAtUtc" IS NOT NULL OR NEW."EndedAtUtc" IS NULL OR (to_jsonb(NEW)-'EndedAtUtc') IS DISTINCT FROM (to_jsonb(OLD)-'EndedAtUtc') THEN RAISE EXCEPTION 'Occupancy may only be closed once' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_occupancy_guard BEFORE INSERT OR UPDATE OR DELETE ON public.bed_occupancies FOR EACH ROW EXECUTE FUNCTION public.medicare_occupancy_guard();

CREATE FUNCTION public.medicare_transfer_guard() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 PERFORM 1 FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
 IF NOT public.medicare_stay_role(NEW."ActorUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist bed allocation required' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_transfer_guard BEFORE INSERT ON public.bed_transfers FOR EACH ROW EXECUTE FUNCTION public.medicare_transfer_guard();

CREATE FUNCTION public.medicare_stay_occupancy_check() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE admission_id uuid; a public.admissions; opens integer; t public.bed_transfers;
BEGIN
 IF TG_TABLE_NAME='admissions' THEN admission_id:=NEW."Id"; ELSE admission_id:=NEW."AdmissionId"; END IF;
 SELECT * INTO a FROM public.admissions WHERE "Id"=admission_id;
 SELECT count(*) INTO opens FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "EndedAtUtc" IS NULL;
 IF (a."Status"='Discharged' AND opens<>0) OR (a."Status"<>'Discharged' AND opens<>1) THEN RAISE EXCEPTION 'Admission and current bed allocation must balance' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.bed_occupancies o JOIN public.bed_transfers tr ON tr."AdmissionId"=o."AdmissionId" AND tr."ToBedId"=o."BedId" AND tr."OccurredAtUtc"=o."StartedAtUtc" AND tr."FromBedId" IS NULL WHERE o."AdmissionId"=admission_id AND o."StartedAtUtc"=a."AdmittedAtUtc") THEN RAISE EXCEPTION 'Initial bed assignment history required' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.bed_occupancies o WHERE o."AdmissionId"=admission_id AND o."EndedAtUtc" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.bed_transfers tr WHERE tr."AdmissionId"=o."AdmissionId" AND tr."FromBedId"=o."BedId" AND tr."OccurredAtUtc"=o."EndedAtUtc")) THEN RAISE EXCEPTION 'Bed closure transfer history required' USING ERRCODE='23514'; END IF;
 IF EXISTS(SELECT 1 FROM public.bed_occupancies o WHERE o."AdmissionId"=admission_id AND NOT EXISTS(SELECT 1 FROM public.bed_transfers tr WHERE tr."AdmissionId"=o."AdmissionId" AND tr."ToBedId"=o."BedId" AND tr."OccurredAtUtc"=o."StartedAtUtc")) THEN RAISE EXCEPTION 'Bed opening transfer history required' USING ERRCODE='23514'; END IF;
 FOR t IN SELECT * FROM public.bed_transfers WHERE "AdmissionId"=admission_id LOOP
  IF t."FromBedId" IS NULL THEN
   IF t."OccurredAtUtc"<>a."AdmittedAtUtc" THEN RAISE EXCEPTION 'Invalid initial assignment time' USING ERRCODE='23514'; END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "BedId"=t."FromBedId" AND "EndedAtUtc"=t."OccurredAtUtc") THEN RAISE EXCEPTION 'Transfer source must close at the transfer time' USING ERRCODE='23514'; END IF;
  IF t."ToBedId" IS NULL THEN
   IF a."Status"<>'Discharged' OR t."OccurredAtUtc" IS DISTINCT FROM a."DischargedAtUtc" THEN RAISE EXCEPTION 'Discharge checkout and bed release must agree' USING ERRCODE='23514'; END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.bed_occupancies WHERE "AdmissionId"=admission_id AND "BedId"=t."ToBedId" AND "StartedAtUtc"=t."OccurredAtUtc") THEN RAISE EXCEPTION 'Transfer destination must open at the transfer time' USING ERRCODE='23514'; END IF;
 END LOOP;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER medicare_admission_bed_check AFTER INSERT OR UPDATE ON public.admissions DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();
CREATE CONSTRAINT TRIGGER medicare_occupancy_history_check AFTER INSERT OR UPDATE ON public.bed_occupancies DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();
CREATE CONSTRAINT TRIGGER medicare_transfer_history_check AFTER INSERT ON public.bed_transfers DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_occupancy_check();

CREATE FUNCTION public.medicare_outpatient_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE owner_account text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Outpatient history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW."Status"<>'Registered' OR NOT public.medicare_stay_role(NEW."RegisteredByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist visit registration required' USING ERRCODE='23514'; END IF;
  PERFORM 1 FROM public.patients p JOIN public.doctors d ON d."Id"=NEW."DoctorId" JOIN public.departments dept ON dept."Id"=d."DepartmentId" WHERE p."Id"=NEW."PatientId" AND NOT p."IsArchived" AND d."IsActive" AND dept."IsActive" AND d."IdentityUserId" IS NOT NULL FOR UPDATE OF p,d,dept;
  IF NOT FOUND THEN RAISE EXCEPTION 'Visit patient or doctor is ineligible' USING ERRCODE='23514'; END IF;
  IF NEW."AppointmentId" IS NOT NULL THEN
   PERFORM 1 FROM public.appointments WHERE "Id"=NEW."AppointmentId" AND "PatientId"=NEW."PatientId" AND "DoctorId"=NEW."DoctorId" AND "Status" IN ('Scheduled','Completed') FOR UPDATE;
   IF NOT FOUND THEN RAISE EXCEPTION 'Matching eligible appointment required' USING ERRCODE='23514'; END IF;
  END IF;
 ELSE
  IF (to_jsonb(NEW)-'Status'-'Version'-'StartedAtUtc'-'StartedByUserId'-'CompletedAtUtc'-'CompletedByUserId'-'CancelledAtUtc'-'CancelledByUserId'-'CancellationReason') IS DISTINCT FROM (to_jsonb(OLD)-'Status'-'Version'-'StartedAtUtc'-'StartedByUserId'-'CompletedAtUtc'-'CompletedByUserId'-'CancelledAtUtc'-'CancelledByUserId'-'CancellationReason') THEN RAISE EXCEPTION 'Visit identity is permanent' USING ERRCODE='23514'; END IF;
  IF OLD."Status" IN ('Completed','Cancelled') THEN RAISE EXCEPTION 'Terminal outpatient history is immutable' USING ERRCODE='23514'; END IF;
  IF OLD."Status"=NEW."Status" THEN
   IF (to_jsonb(NEW)-'Version') IS DISTINCT FROM (to_jsonb(OLD)-'Version') THEN RAISE EXCEPTION 'Invalid visit transition' USING ERRCODE='23514'; END IF;
  ELSIF OLD."Status"='Registered' AND NEW."Status"='Cancelled' THEN
   IF NOT public.medicare_stay_role(NEW."CancelledByUserId",'Receptionist') THEN RAISE EXCEPTION 'Receptionist cancellation required' USING ERRCODE='23514'; END IF;
  ELSIF (OLD."Status"='Registered' AND NEW."Status"='InConsultation') OR (OLD."Status"='InConsultation' AND NEW."Status"='Completed') THEN
   SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=NEW."DoctorId" AND "IsActive" FOR UPDATE;
   IF owner_account IS NULL OR NOT public.medicare_stay_role(owner_account,'Doctor') OR (NEW."Status"='InConsultation' AND NEW."StartedByUserId" IS DISTINCT FROM owner_account) OR (NEW."Status"='Completed' AND (NEW."CompletedByUserId" IS DISTINCT FROM owner_account OR NEW."StartedAtUtc" IS DISTINCT FROM OLD."StartedAtUtc" OR NEW."StartedByUserId" IS DISTINCT FROM OLD."StartedByUserId")) THEN RAISE EXCEPTION 'Assigned treating doctor transition required' USING ERRCODE='23514'; END IF;
   IF NEW."Status"='InConsultation' AND EXISTS(SELECT 1 FROM public.patients WHERE "Id"=NEW."PatientId" AND "IsArchived") THEN RAISE EXCEPTION 'Archived patient cannot start consultation' USING ERRCODE='23514'; END IF;
  ELSE RAISE EXCEPTION 'Invalid visit transition' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_outpatient_guard BEFORE INSERT OR UPDATE OR DELETE ON public.outpatient_visits FOR EACH ROW EXECUTE FUNCTION public.medicare_outpatient_guard();

CREATE FUNCTION public.medicare_stay_clinical_guard() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE patient uuid; doctor uuid; owner_account text; state text;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Stay clinical history is permanent' USING ERRCODE='23514'; END IF;
 IF TG_TABLE_NAME='admission_clinical_notes' THEN
  SELECT "PatientId","DoctorId","Status" INTO patient,doctor,state FROM public.admissions WHERE "Id"=NEW."AdmissionId" FOR UPDATE;
  IF state IS DISTINCT FROM 'Admitted' THEN RAISE EXCEPTION 'Cleared admission clinical notes are immutable' USING ERRCODE='23514'; END IF;
  IF length(btrim(NEW."AdmissionReason"))=0 THEN RAISE EXCEPTION 'Clinical admission reason required' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND (NEW."AdmissionId"<>OLD."AdmissionId" OR NEW."AuthorUserId"<>OLD."AuthorUserId") THEN RAISE EXCEPTION 'Clinical note identity is permanent' USING ERRCODE='23514'; END IF;
 ELSE
  SELECT "PatientId","DoctorId","Status" INTO patient,doctor,state FROM public.outpatient_visits WHERE "Id"=NEW."OutpatientVisitId" FOR UPDATE;
  IF state IS DISTINCT FROM 'InConsultation' THEN RAISE EXCEPTION 'Outpatient clinical notes require an active consultation' USING ERRCODE='23514'; END IF;
  IF TG_OP='UPDATE' AND (NEW."OutpatientVisitId"<>OLD."OutpatientVisitId" OR NEW."AuthorUserId"<>OLD."AuthorUserId") THEN RAISE EXCEPTION 'Clinical note identity is permanent' USING ERRCODE='23514'; END IF;
 END IF;
 SELECT "IdentityUserId" INTO owner_account FROM public.doctors WHERE "Id"=doctor AND "IsActive" FOR UPDATE;
 IF owner_account IS NULL OR owner_account IS DISTINCT FROM NEW."AuthorUserId" OR NOT public.medicare_stay_role(owner_account,'Doctor') THEN RAISE EXCEPTION 'Assigned treating doctor clinical author required' USING ERRCODE='23514'; END IF;
 IF NEW."MedicalRecordId" IS NOT NULL AND NOT EXISTS(SELECT 1 FROM public.medical_records mr WHERE mr."Id"=NEW."MedicalRecordId" AND mr."PatientId"=patient AND EXISTS(SELECT 1 FROM public.medical_record_revisions r WHERE r."MedicalRecordId"=mr."Id" AND r."FinalizedAtUtc" IS NOT NULL) AND EXISTS(SELECT 1 FROM public.appointments a JOIN public.doctors d ON d."Id"=a."DoctorId" WHERE a."PatientId"=patient AND a."Status" IN ('Scheduled','Completed') AND d."IsActive" AND d."IdentityUserId"=owner_account)) THEN RAISE EXCEPTION 'Existing authorized finalized EMR care reference required' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER medicare_admission_clinical_guard BEFORE INSERT OR UPDATE OR DELETE ON public.admission_clinical_notes FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_clinical_guard();
CREATE TRIGGER medicare_outpatient_clinical_guard BEFORE INSERT OR UPDATE OR DELETE ON public.outpatient_clinical_notes FOR EACH ROW EXECUTE FUNCTION public.medicare_stay_clinical_guard();
""";
}
