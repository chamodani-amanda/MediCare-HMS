using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;

internal static class MedicalRecordDatabaseChecks
{
    private static string stage = "target verification";
    public static async Task Run(string[] args)
    {
        try
        {
            var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web"))
                .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
                .AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
            var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));
            Check(cs.Database == "medicare_dev", "Configured target mismatch; connection refused."); cs.Timeout = 10;
            await using var connection = new NpgsqlConnection(cs.ConnectionString); await connection.OpenAsync();
            await using (var cmd = new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'", connection))
                Check((bool?)await cmd.ExecuteScalarAsync() == true, "Actual target/schema mismatch.");
            Console.WriteLine("Confirmed connected database: medicare_dev; target schema: public.");
            var before = await Snapshot(connection);
            Check(before.ContainsKey("patients") && before.ContainsKey("doctors") && before.ContainsKey("appointments") && before.Keys.Count(k=>k.StartsWith("AspNet",StringComparison.Ordinal))==7, "Existing application tables missing.");
            if (args.Contains("--apply-approved-emr"))
            {
                stage = "final migration and backup safety preflight";
                await using (var tx = await connection.BeginTransactionAsync())
                {
                    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, tx)) await readOnly.ExecuteNonQueryAsync();
                    await using var context = Context(connection); await context.Database.UseTransactionAsync(tx);
                    await MedicalRecordPreflight.Verify(context, connection, tx);
                    await tx.CommitAsync();
                }
                // Guard again immediately before applying exactly the user-approved migration.
                await using var application = Context(connection);
                Check((await application.Database.GetPendingMigrationsAsync()).SequenceEqual(new[] { MedicalRecordPreflight.Migration }), "Unexpected pending migrations; stopped before application.");
                Console.WriteLine("Trigger review PASS: all three triggers attach only to newly created EMR tables; existing-table rows are not rewritten and existing tables receive no triggers.");
                stage = "approved EMR migration application";
                await application.GetService<IMigrator>().MigrateAsync(MedicalRecordPreflight.Migration);
                Console.WriteLine("Applied migration: " + MedicalRecordPreflight.Migration);
            }
            stage = "EMR schema verification"; await Schema(connection); await Preserved(connection, before);
            if (args.Contains("--integration-emr"))
            {
                stage = "rollback-only PostgreSQL EMR integration";
                await Integration(connection); await Preserved(connection, before);
            }
            Console.WriteLine("SUCCESS: requested EMR checks passed; existing application rows preserved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("STOPPED during " + stage + ": " + ex.GetType().Name + (ex is VerificationFailure ? "; " + ex.Message : ex is PostgresException pg ? "; SQLSTATE=" + pg.SqlState : "") + ". Provider details suppressed to protect clinical data and secrets.");
            Environment.ExitCode = 1;
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new VerificationFailure(message); }
    internal static MediCareDbContext Context(NpgsqlConnection connection) => new(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql(connection).Options);
    internal static async Task<Dictionary<string,(long Count,string Hash)>> Snapshot(NpgsqlConnection connection)
    {
        var names = new List<string>();
        await using (var cmd = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename<>'__EFMigrationsHistory' ORDER BY tablename", connection))
        await using (var reader = await cmd.ExecuteReaderAsync()) while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        var result = new Dictionary<string,(long,string)>();
        foreach (var name in names)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
            await using var cmd = new NpgsqlCommand("SELECT row_to_json(t)::text FROM public.\"" + name.Replace("\"", "\"\"") + "\" t ORDER BY row_to_json(t)::text COLLATE \"C\"", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { hash.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0) + "\n")); count++; }
            result[name] = (count, Convert.ToHexString(hash.GetHashAndReset()));
        }
        return result;
    }
    internal static async Task Preserved(NpgsqlConnection connection, Dictionary<string,(long Count,string Hash)> before)
    {
        var after = await Snapshot(connection);
        foreach (var pair in before)
        {
            Check(after.TryGetValue(pair.Key, out var value) && value == pair.Value, "Existing rows changed: " + pair.Key);
            Console.WriteLine("Preserved: " + pair.Key + "; rows=" + pair.Value.Count + "; full-row SHA-256 fingerprint unchanged.");
        }
    }
    private static async Task Schema(NpgsqlConnection connection)
    {
        await using var db = Context(connection);
        Check((await db.Database.GetAppliedMigrationsAsync()).Contains(MedicalRecordPreflight.Migration) && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Unexpected migration history.");
        var checks = new Dictionary<string,string>
        {
            ["three EMR tables / expected columns"] = "SELECT count(*)=30 FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('medical_records','medical_record_revisions','medical_prescriptions')",
            ["three primary keys"] = "SELECT count(*)=3 FROM pg_constraint WHERE conrelid IN ('public.medical_records'::regclass,'public.medical_record_revisions'::regclass,'public.medical_prescriptions'::regclass) AND contype='p' AND convalidated",
            ["seven restrictive foreign keys"] = "SELECT count(*)=7 AND bool_and(confdeltype='r' AND convalidated) FROM pg_constraint WHERE conrelid IN ('public.medical_records'::regclass,'public.medical_record_revisions'::regclass,'public.medical_prescriptions'::regclass) AND contype='f'",
            ["four validated checks"] = "SELECT count(*)=4 AND bool_and(convalidated) FROM pg_constraint WHERE conrelid IN ('public.medical_records'::regclass,'public.medical_record_revisions'::regclass,'public.medical_prescriptions'::regclass) AND contype='c'",
            ["nine indexes plus primary keys"] = "SELECT count(*)=12 AND bool_and(indisvalid) FROM pg_index WHERE indrelid IN ('public.medical_records'::regclass,'public.medical_record_revisions'::regclass,'public.medical_prescriptions'::regclass)",
            ["one draft partial unique index"] = "SELECT EXISTS(SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE c.relname='IX_medical_record_revisions_MedicalRecordId' AND i.indisunique AND i.indisvalid AND pg_get_expr(i.indpred,i.indrelid)='(\"FinalizedAtUtc\" IS NULL)')",
            ["two UUID concurrency columns"] = "SELECT count(*)=2 FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('medical_records','medical_record_revisions') AND column_name='Version' AND udt_name='uuid' AND is_nullable='NO'",
            ["three enabled row history-protection triggers"] = "SELECT count(*)=3 AND bool_and(t.tgenabled='O' AND t.tgtype=31) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_proc p ON p.oid=t.tgfoid WHERE NOT t.tgisinternal AND c.relnamespace='public'::regnamespace AND ((c.relname='medical_records' AND t.tgname='medicare_emr_record_guard' AND p.proname=t.tgname) OR (c.relname='medical_record_revisions' AND t.tgname='medicare_emr_revision_guard' AND p.proname=t.tgname) OR (c.relname='medical_prescriptions' AND t.tgname='medicare_emr_prescription_guard' AND p.proname=t.tgname))",
            ["no EMR triggers on existing tables"] = "SELECT NOT EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_class c ON c.oid=t.tgrelid WHERE p.pronamespace='public'::regnamespace AND p.proname IN ('medicare_emr_record_guard','medicare_emr_revision_guard','medicare_emr_prescription_guard') AND c.relname NOT IN ('medical_records','medical_record_revisions','medical_prescriptions'))"
        };
        foreach (var check in checks)
        {
            await using var cmd = new NpgsqlCommand(check.Value, connection);
            Check((bool?)await cmd.ExecuteScalarAsync() == true, "Schema mismatch: " + check.Key);
            Console.WriteLine("Schema PASS: " + check.Key);
        }
        // Verify each named index, column and FK definition against the approved EF migration.
        var assembly=db.GetService<IMigrationsAssembly>(); var migration=assembly.CreateMigration(assembly.Migrations[MedicalRecordPreflight.Migration],db.Database.ProviderName!);
        foreach(var table in migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>())
        {
            foreach(var column in table.Columns)
            {
                await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column AND is_nullable=@nullable",connection);
                cmd.Parameters.AddWithValue("table",table.Name); cmd.Parameters.AddWithValue("column",column.Name); cmd.Parameters.AddWithValue("nullable",column.IsNullable?"YES":"NO");
                Check((bool?)await cmd.ExecuteScalarAsync()==true,"Column mismatch: "+table.Name+"."+column.Name);
            }
            foreach(var fk in table.ForeignKeys)
            {
                await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_class p ON p.oid=c.confrelid WHERE t.relnamespace='public'::regnamespace AND t.relname=@table AND c.conname=@name AND p.relname=@parent AND c.confdeltype='r' AND c.convalidated",connection);
                cmd.Parameters.AddWithValue("table",table.Name); cmd.Parameters.AddWithValue("name",fk.Name!); cmd.Parameters.AddWithValue("parent",fk.PrincipalTable);
                Check((bool?)await cmd.ExecuteScalarAsync()==true,"Relationship mismatch.");
            }
        }
        foreach(var index in migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>())
        {
            await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class t ON t.oid=i.indrelid WHERE t.relnamespace='public'::regnamespace AND c.relname=@name AND t.relname=@table AND i.indisvalid AND i.indisunique=@unique",connection);
            cmd.Parameters.AddWithValue("name",index.Name);cmd.Parameters.AddWithValue("table",index.Table);cmd.Parameters.AddWithValue("unique",index.IsUnique);
            Check((bool?)await cmd.ExecuteScalarAsync()==true,"Named index mismatch.");
        }
        Console.WriteLine("Schema PASS: every expected column, named index and foreign-key target matched approved migration; no migrations pending.");
    }

    private static async Task Integration(NpgsqlConnection connection)
    {
        await using var tx=await connection.BeginTransactionAsync();
        try
        {
            var userId=Guid.NewGuid().ToString(); var otherUser=Guid.NewGuid().ToString(); var prefix="EMR-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var patient=new Patient { PatientNumber=prefix,FirstName="Synthetic rollback",LastName="Patient" };
            var department=new Department {Name=prefix,NormalizedName=prefix};
            var doctor=new Doctor { Department=department,IdentityUserId=userId,RegistrationNumber=prefix,FirstName="Synthetic rollback",LastName="Doctor" };
            var otherDoctor=new Doctor { Department=department,IdentityUserId=otherUser,RegistrationNumber=prefix+"B",FirstName="Synthetic rollback",LastName="Other doctor" };
            var now=DateTime.UtcNow; var start=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute/15*15,0,DateTimeKind.Utc).AddHours(-1);
            var appointment=new Appointment { Patient=patient,Doctor=doctor,BookingToken=Guid.NewGuid(),StartAtUtc=start,EndAtUtc=start.AddMinutes(30),DurationMinutes=30 };
            await using(var seed=Context(connection))
            {
                await seed.Database.UseTransactionAsync(tx);
                seed.Users.AddRange(new IdentityUser { Id=userId,UserName=prefix,NormalizedUserName=prefix,SecurityStamp=Guid.NewGuid().ToString() },new IdentityUser { Id=otherUser,UserName=prefix+"B",NormalizedUserName=prefix+"B",SecurityStamp=Guid.NewGuid().ToString() });
                var role=await seed.Roles.SingleAsync(r=>r.Name=="Doctor");
                seed.UserRoles.AddRange(new IdentityUserRole<string>{UserId=userId,RoleId=role.Id},new IdentityUserRole<string>{UserId=otherUser,RoleId=role.Id});
                seed.AddRange(appointment,otherDoctor); await seed.SaveChangesAsync();
            }
            MedicalRecordForm Form()=>new() {AppointmentId=appointment.Id,CreationToken=Guid.NewGuid(),Symptoms="Synthetic rollback symptoms",Observations="Synthetic rollback observations",Diagnosis="Synthetic rollback diagnosis",Treatment="Synthetic rollback treatment",Prescriptions=new() {new(){Medication="Synthetic test medication",Dosage="Synthetic dose",Route="Synthetic route",Frequency="Synthetic frequency",Duration="Synthetic duration"}}};
            async Task<IActionResult> Invoke(Func<MedicalRecordsController,Task<IActionResult>> action,string? actor=null)
            {
                await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);
                var controller=new MedicalRecordsController(db,TimeProvider.System) {ControllerContext=new() {HttpContext=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,actor??userId),new Claim(ClaimTypes.Role,"Doctor")},"RollbackIntegration"))}}};
                return await action(controller);
            }
            async Task<MedicalRecordRevision> Revision(int number=1)
            { await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);return await db.MedicalRecordRevisions.AsNoTracking().Include(r=>r.Prescriptions).SingleAsync(r=>r.MedicalRecord.AppointmentId==appointment.Id && r.Number==number); }
            var form=Form();Check(await Invoke(c=>c.Create(form)) is RedirectToActionResult,"Valid record creation failed.");
            Check(await Invoke(c=>c.Create(form)) is RedirectToActionResult,"Idempotent repeat failed.");
            var original=await Revision();
            Check(await Invoke(c=>c.Details(original.Id)) is ViewResult,"Record retrieval failed.");
            Check(await Invoke(c=>c.Pdf(original.Id)) is FileContentResult,"Private PDF failed.");
            Console.WriteLine("PostgreSQL PASS: appointment-linked creation, structured prescription, retrieval, PDF and duplicate-submit protection.");

            foreach(var action in new Func<MedicalRecordsController,Task<IActionResult>>[] {c=>c.Details(original.Id),c=>c.Pdf(original.Id),c=>c.Edit(original.Id),c=>c.Edit(original.Id,Form()),c=>c.Finalize(original.Id,original.Version),c=>c.Amend(original.Id,original.Version,"Synthetic unauthorized attempt"),c=>c.Create(Form())})
                Check(await Invoke(action,otherUser) is NotFoundResult,"Unrelated doctor was not denied.");
            Console.WriteLine("PostgreSQL PASS: unrelated linked doctor cannot retrieve, export, edit, finalize, amend or create this patient's encounter. Role/antiforgery boundaries are covered by HTTP tests.");

            var edit=Form();edit.Version=original.Version;edit.Diagnosis="Synthetic updated diagnosis";
            Check(await Invoke(c=>c.Edit(original.Id,edit)) is RedirectToActionResult,"Draft prescription replacement failed.");
            Check(await Invoke(c=>c.Edit(original.Id,edit)) is ViewResult,"Stale edit was not rejected.");
            var changed=await Revision();Check(changed.Diagnosis==edit.Diagnosis && changed.Prescriptions.Count==1,"Draft update persistence failed.");
            Check(await Invoke(c=>c.Finalize(changed.Id,changed.Version)) is RedirectToActionResult,"Finalization failed.");
            var final=await Revision();Check(final.FinalizedAtUtc!=null,"Finalization timestamp missing.");
            Check(await Invoke(c=>c.Edit(final.Id)) is NotFoundResult,"Finalized record exposed as editable.");
            Check(await Invoke(c=>c.Amend(final.Id,final.Version,"Synthetic correction reason")) is RedirectToActionResult,"Amendment failed.");
            var amendment=await Revision(2);Check(amendment.FinalizedAtUtc==null && amendment.AmendmentReason=="Synthetic correction reason" && amendment.Prescriptions.Count==1,"Amendment snapshot invalid.");
            Check(await Invoke(c=>c.Amend(final.Id,final.Version,"Synthetic duplicate reason")) is ConflictObjectResult,"Duplicate amendment not rejected.");
            var amended=Form();amended.Version=amendment.Version;amended.AmendmentReason="Synthetic correction reason";amended.Treatment="Synthetic amended treatment";
            Check(await Invoke(c=>c.Edit(amendment.Id,amended)) is RedirectToActionResult,"Amendment edit failed.");
            var editedAmendment=await Revision(2);
            Check(await Invoke(c=>c.Finalize(editedAmendment.Id,editedAmendment.Version)) is RedirectToActionResult,"Amendment finalization failed.");
            var preserved=await Revision();Check(preserved.Diagnosis==final.Diagnosis && preserved.Treatment==final.Treatment && preserved.FinalizedAtUtc==final.FinalizedAtUtc,"Original finalized history changed.");
            Console.WriteLine("PostgreSQL PASS: draft editing/prescription replacement, stale form rejection, finalization, reasoned amendment, duplicate amendment protection and immutable original history.");

            async Task Reject(string label,string sql,string state,params (string,object)[] parameters)
            {
                await tx.SaveAsync("emr_probe");
                try
                {
                    await using var cmd=new NpgsqlCommand(sql,connection,tx);foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Item1,p.Item2);
                    await cmd.ExecuteNonQueryAsync();throw new VerificationFailure("Database protection did not reject: "+label);
                }
                catch(PostgresException ex) when(ex.SqlState==state){Console.WriteLine("PostgreSQL guard PASS: "+label+"; SQLSTATE="+state);}
                finally {await tx.RollbackAsync("emr_probe");await tx.ReleaseAsync("emr_probe");}
            }
            await Reject("finalized revision update","UPDATE medical_record_revisions SET \"Diagnosis\"='Synthetic forbidden update' WHERE \"Id\"=@id","23514",("id",final.Id));
            await Reject("finalized revision delete","DELETE FROM medical_record_revisions WHERE \"Id\"=@id","23514",("id",final.Id));
            await Reject("finalized prescription update","UPDATE medical_prescriptions SET \"Dosage\"='Synthetic forbidden dose' WHERE \"Id\"=@id","23514",("id",final.Prescriptions[0].Id));
            await Reject("finalized prescription delete","DELETE FROM medical_prescriptions WHERE \"Id\"=@id","23514",("id",final.Prescriptions[0].Id));
            await Reject("finalized prescription insertion","INSERT INTO medical_prescriptions (\"Id\",\"MedicalRecordRevisionId\",\"Position\",\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\") VALUES (@id,@revision,1,'Synthetic','Test','Test','Test','Test')","23514",("id",Guid.NewGuid()),("revision",final.Id));
            await Reject("encounter identity change","UPDATE medical_records SET \"DoctorId\"=@doctor WHERE \"Id\"=@id","23514",("doctor",otherDoctor.Id),("id",final.MedicalRecordId));
            await Reject("encounter delete","DELETE FROM medical_records WHERE \"Id\"=@id","23514",("id",final.MedicalRecordId));
            await Reject("mismatched encounter author","INSERT INTO medical_records (\"Id\",\"AppointmentId\",\"PatientId\",\"DoctorId\",\"AuthorUserId\",\"CreationToken\",\"Version\",\"CreatedAtUtc\") VALUES (@id,@appointment,@patient,@doctor,@author,@token,@version,now())","23514",("id",Guid.NewGuid()),("appointment",appointment.Id),("patient",patient.Id),("doctor",doctor.Id),("author",otherUser),("token",Guid.NewGuid()),("version",Guid.NewGuid()));
            await Reject("invalid revision sequence","INSERT INTO medical_record_revisions (\"Id\",\"MedicalRecordId\",\"Number\",\"AuthorUserId\",\"Symptoms\",\"Observations\",\"Diagnosis\",\"Treatment\",\"AmendmentReason\",\"CreatedAtUtc\",\"UpdatedAtUtc\",\"Version\") VALUES (@id,@record,4,@author,'','','','','Synthetic',now(),now(),@version)","23514",("id",Guid.NewGuid()),("record",final.MedicalRecordId),("author",userId),("version",Guid.NewGuid()));

            // A new draft permits genuine PostgreSQL stale-version testing without altering finalized rows.
            var latest=await Revision(2);Check(await Invoke(c=>c.Amend(latest.Id,latest.Version,"Synthetic concurrency probe")) is RedirectToActionResult,"Concurrency draft setup failed.");
            var draft=await Revision(3);
            await Reject("draft revision author change","UPDATE medical_record_revisions SET \"AuthorUserId\"=@author WHERE \"Id\"=@id","23514",("author",otherUser),("id",draft.Id));
            await Reject("incomplete finalization","UPDATE medical_record_revisions SET \"Diagnosis\"='',\"FinalizedAtUtc\"=now() WHERE \"Id\"=@id","23514",("id",draft.Id));
            await Reject("duplicate prescription position","INSERT INTO medical_prescriptions (\"Id\",\"MedicalRecordRevisionId\",\"Position\",\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\") VALUES (@id,@revision,0,'Synthetic','Test','Test','Test','Test')","23505",("id",Guid.NewGuid()),("revision",draft.Id));
            await Reject("blank prescription fields","INSERT INTO medical_prescriptions (\"Id\",\"MedicalRecordRevisionId\",\"Position\",\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\") VALUES (@id,@revision,1,'','Test','Test','Test','Test')","23514",("id",Guid.NewGuid()),("revision",draft.Id));
            await using(var first=Context(connection))
            await using(var stale=Context(connection))
            {
                await first.Database.UseTransactionAsync(tx);await stale.Database.UseTransactionAsync(tx);
                var a=await first.MedicalRecordRevisions.SingleAsync(r=>r.Id==draft.Id);var b=await stale.MedicalRecordRevisions.SingleAsync(r=>r.Id==draft.Id);
                a.Observations="Synthetic winning edit";a.Version=Guid.NewGuid();await first.SaveChangesAsync();
                var beforeFailure=await Snapshot(connection); b.Observations="Synthetic stale edit";b.Version=Guid.NewGuid();
                stale.SecurityAuditLogs.Add(new SecurityAuditLog {Action="SyntheticStaleEmrWrite",ActorUserId=userId,TargetUserId=draft.Id.ToString(),Succeeded=true});
                try {await stale.SaveChangesAsync();throw new VerificationFailure("PostgreSQL stale revision was accepted.");}
                catch(DbUpdateConcurrencyException){Console.WriteLine("PostgreSQL PASS: actual stale EF revision write rejected.");}
                await Preserved(connection,beforeFailure);
            }
            // Shared application credentials are privileged SQL access, not clinical role authorization.
            // The above SQL guards test immutability/integrity; actual role filters run in MVC HTTP tests.
        }
        finally {await tx.RollbackAsync();Console.WriteLine("Rolled back all synthetic clinical fixtures, Identity links, token changes and audit events.");}
    }
    private sealed class VerificationFailure(string message):Exception(message);
}
