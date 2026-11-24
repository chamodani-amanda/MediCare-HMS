using System.Security.Claims;
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
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static MedicalRecordDatabaseChecks;

internal static class LaboratoryDatabaseChecks
{
    private static string stage="target verification";
    internal static async Task Run(string[] args)
    {
        try
        {
            var config=new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true)
                .AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
            var cs=new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));
            Require(cs.Database=="medicare_dev","Configured database mismatch; connection refused.");cs.Timeout=10;
            await using var connection=new NpgsqlConnection(cs.ConnectionString);await connection.OpenAsync();
            await using(var cmd=new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true,"Actual database/schema mismatch.");
            Console.WriteLine("Confirmed connected database: medicare_dev; target schema: public.");
            var before=await Snapshot(connection);Require(before.ContainsKey("patients")&&before.ContainsKey("medical_records")&&before.Keys.Count(k=>k.StartsWith("AspNet",StringComparison.Ordinal))==7,"Existing tables missing.");
            if(args.Contains("--apply-approved-lab"))
            {
                stage="final laboratory migration and backup preflight";
                await using(var tx=await connection.BeginTransactionAsync())
                {
                    await using(var cmd=new NpgsqlCommand("SET TRANSACTION READ ONLY",connection,tx))await cmd.ExecuteNonQueryAsync();
                    await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);await LaboratoryPreflight.Verify(db,connection,tx);await tx.CommitAsync();
                }
                await using var application=Context(connection);
                Require((await application.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{LaboratoryPreflight.Target}),"Unexpected pending migrations; stopped before application.");
                stage="approved laboratory migration application";
                await application.GetService<IMigrator>().MigrateAsync(LaboratoryPreflight.Target);
                Console.WriteLine("Applied migration: "+LaboratoryPreflight.Target);
            }
            stage="laboratory schema verification";await Schema(connection);await Preserved(connection,before);
            if(args.Contains("--integration-lab"))
            {
                var integrationBefore=await Snapshot(connection);stage="rollback-only PostgreSQL laboratory integration";
                await Integration(connection);await Preserved(connection,integrationBefore);
            }
            Console.WriteLine("SUCCESS: requested laboratory checks passed; all original records preserved.");
        }
        catch(Exception ex)
        {
            Console.WriteLine("STOPPED during "+stage+": "+ex.GetType().Name+(ex is CheckFailure?"; "+ex.Message:ex is PostgresException p?"; SQLSTATE="+p.SqlState:"")+". Provider details suppressed to protect secrets and clinical data.");
            Environment.ExitCode=1;
        }
    }
    private static void Require(bool value,string message){if(!value)throw new CheckFailure(message);}
    private static async Task Schema(NpgsqlConnection connection)
    {
        await using var db=Context(connection);Require((await db.Database.GetAppliedMigrationsAsync()).Contains(LaboratoryPreflight.Target)&&!(await db.Database.GetPendingMigrationsAsync()).Any(),"Unexpected migration history.");
        var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[LaboratoryPreflight.Target],db.Database.ProviderName!);
        foreach(var table in migration.UpOperations.OfType<CreateTableOperation>())
        {
            await using(var count=new NpgsqlCommand("SELECT count(*)=@count FROM information_schema.columns WHERE table_schema='public' AND table_name=@table",connection))
            {count.Parameters.AddWithValue("count",table.Columns.Count);count.Parameters.AddWithValue("table",table.Name);Require((bool?)await count.ExecuteScalarAsync()==true,"Column count mismatch: "+table.Name);}
            foreach(var column in table.Columns)
            {
                await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column AND is_nullable=@nullable",connection);
                cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("column",column.Name);cmd.Parameters.AddWithValue("nullable",column.IsNullable?"YES":"NO");Require((bool?)await cmd.ExecuteScalarAsync()==true,"Column mismatch.");
            }
            foreach(var fk in table.ForeignKeys)
            {
                await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_class p ON p.oid=c.confrelid WHERE t.relnamespace='public'::regnamespace AND t.relname=@table AND c.conname=@name AND p.relname=@parent AND c.confdeltype='r' AND c.convalidated",connection);
                cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",fk.Name!);cmd.Parameters.AddWithValue("parent",fk.PrincipalTable);Require((bool?)await cmd.ExecuteScalarAsync()==true,"Foreign-key mismatch.");
            }
            foreach(var check in table.CheckConstraints)
            {
                await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid WHERE t.relnamespace='public'::regnamespace AND t.relname=@table AND c.conname=@name AND c.contype='c' AND c.convalidated",connection);
                cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",check.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true,"Check constraint mismatch.");
            }
            await using(var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid WHERE t.relnamespace='public'::regnamespace AND t.relname=@table AND c.contype='p' AND c.convalidated",connection))
            {cmd.Parameters.AddWithValue("table",table.Name);Require((bool?)await cmd.ExecuteScalarAsync()==true,"Primary key mismatch.");}
            Console.WriteLine("Schema PASS: "+table.Name+"; columns/nullability, primary key, restrictive relationships and checks matched migration.");
        }
        foreach(var index in migration.UpOperations.OfType<CreateIndexOperation>())
        {
            await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_class t ON t.oid=i.indrelid WHERE t.relnamespace='public'::regnamespace AND c.relname=@name AND t.relname=@table AND i.indisvalid AND i.indisunique=@unique AND (i.indpred IS NOT NULL)=@filtered",connection);
            cmd.Parameters.AddWithValue("name",index.Name);cmd.Parameters.AddWithValue("table",index.Table);cmd.Parameters.AddWithValue("unique",index.IsUnique);cmd.Parameters.AddWithValue("filtered",index.Filter!=null);Require((bool?)await cmd.ExecuteScalarAsync()==true,"Index mismatch.");
        }
        await using(var cmd=new NpgsqlCommand("""
            SELECT count(*)=4 AND bool_and(t.tgenabled='O' AND
                ((t.tgname IN ('medicare_lab_request_guard','medicare_lab_result_guard') AND t.tgtype=31 AND NOT t.tgdeferrable) OR
                 (t.tgname IN ('medicare_lab_request_completion','medicare_lab_result_completion') AND t.tgtype=21 AND t.tgdeferrable AND t.tginitdeferred)))
            FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
            WHERE c.relnamespace='public'::regnamespace AND c.relname IN ('lab_requests','lab_result_revisions') AND NOT t.tgisinternal
            """,connection))Require((bool?)await cmd.ExecuteScalarAsync()==true,"Laboratory trigger/deferral mismatch.");
        await using(var cmd=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_proc p ON p.oid=t.tgfoid
                WHERE p.pronamespace='public'::regnamespace AND p.proname IN ('medicare_lab_request_guard','medicare_lab_result_guard','medicare_lab_completion_check') AND c.relname NOT IN ('lab_requests','lab_result_revisions'))
            """,connection))Require((bool?)await cmd.ExecuteScalarAsync()==true,"Laboratory guard attached to an existing table.");
        Console.WriteLine("Schema PASS: all named indexes, unique/partial indexes, four enabled row triggers including two deferred completion checks; no new triggers on existing tables; no pending migrations.");
    }

    private static async Task Integration(NpgsqlConnection connection)
    {
        await using var tx=await connection.BeginTransactionAsync();
        try
        {
            var doc=Guid.NewGuid().ToString();var otherDoc=Guid.NewGuid().ToString();var lab=Guid.NewGuid().ToString();var otherLab=Guid.NewGuid().ToString();
            var prefix="LAB-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();var now=DateTime.UtcNow;
            var department=new Department{Name=prefix,NormalizedName=prefix};
            var doctor=new Doctor {Department=department,IdentityUserId=doc,RegistrationNumber=prefix,FirstName="Synthetic rollback",LastName="Doctor"};
            var secondDoctor=new Doctor {Department=department,IdentityUserId=otherDoc,RegistrationNumber=prefix+"B",FirstName="Synthetic rollback",LastName="Other doctor"};
            var patient=new Patient {PatientNumber=prefix,FirstName="Synthetic rollback",LastName="Patient"};
            var start=new DateTime(now.Year,now.Month,now.Day,now.Hour,now.Minute/15*15,0,DateTimeKind.Utc).AddHours(-1);
            var appointment=new Appointment {Patient=patient,Doctor=doctor,BookingToken=Guid.NewGuid(),StartAtUtc=start,EndAtUtc=start.AddMinutes(30),DurationMinutes=30};
            var encounter=new MedicalRecord {Patient=patient,Doctor=doctor,Appointment=appointment,AuthorUserId=doc,CreationToken=Guid.NewGuid(),CreatedAtUtc=now};
            var numeric=new LabTest {Code=prefix,Name="Synthetic numeric test",Category="Synthetic",SampleType="Synthetic sample",ResultKind=LabResultKind.Numeric,Unit="Synthetic unit",ReferenceRange="Configured synthetic range",CreatedAtUtc=now,UpdatedAtUtc=now};
            var text=new LabTest {Code=prefix+"T",Name="Synthetic text test",Category="Synthetic",SampleType="Synthetic sample",ResultKind=LabResultKind.Text,CreatedAtUtc=now,UpdatedAtUtc=now};
            await using(var seed=Context(connection))
            {
                await seed.Database.UseTransactionAsync(tx);
                foreach(var user in new[]{doc,otherDoc,lab,otherLab})seed.Users.Add(new IdentityUser {Id=user,UserName="synthetic-"+user,NormalizedUserName="SYNTHETIC-"+user.ToUpperInvariant(),SecurityStamp=Guid.NewGuid().ToString()});
                var doctorRole=await seed.Roles.SingleAsync(r=>r.Name=="Doctor");var labRole=await seed.Roles.SingleAsync(r=>r.Name=="Laboratory Staff");
                foreach(var user in new[]{doc,otherDoc})seed.UserRoles.Add(new(){UserId=user,RoleId=doctorRole.Id});
                foreach(var user in new[]{lab,otherLab})seed.UserRoles.Add(new(){UserId=user,RoleId=labRole.Id});
                seed.AddRange(encounter,secondDoctor,numeric,text);await seed.SaveChangesAsync();
            }
            async Task<IActionResult> Invoke(Func<LaboratoryController,Task<IActionResult>> action,string? actor=null,bool staff=false)
            {
                await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);
                var controller=new LaboratoryController(db,TimeProvider.System){ControllerContext=new(){HttpContext=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,actor??doc),new Claim(ClaimTypes.Role,staff?"Laboratory Staff":"Doctor")},"RollbackIntegration"))}}};
                return await action(controller);
            }
            async Task<LabRequest> Request(Guid id){await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);return await db.LabRequests.AsNoTracking().SingleAsync(r=>r.Id==id);}
            async Task<LabResultRevision> Result(Guid request,int number=1){await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);return await db.LabResultRevisions.AsNoTracking().SingleAsync(r=>r.LabRequestId==request&&r.Number==number);}
            async Task Flush(){await using(var cmd=new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE",connection,tx))await cmd.ExecuteNonQueryAsync();await using(var cmd=new NpgsqlCommand("SET CONSTRAINTS ALL DEFERRED",connection,tx))await cmd.ExecuteNonQueryAsync();}
            async Task<Guid> Create(Guid test)
            {
                var form=new LabRequestForm {MedicalRecordId=encounter.Id,LabTestId=test,CreationToken=Guid.NewGuid()};
                Require(await Invoke(c=>c.Create(form)) is RedirectToActionResult,"Valid laboratory request failed.");
                Require(await Invoke(c=>c.Create(form)) is RedirectToActionResult,"Request idempotency failed.");
                await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);var id=await db.LabRequests.Where(r=>r.CreationToken==form.CreationToken).Select(r=>r.Id).SingleAsync();await Flush();return id;
            }
            async Task Progress(Guid id,string command)
            {var request=await Request(id);Require(await Invoke(c=>c.Progress(id,request.Version,command),lab,true) is RedirectToActionResult,"Sample step failed: "+command);await Flush();}
            var id=await Create(numeric.Id);var initial=await Request(id);
            Require(await Invoke(c=>c.Details(id),otherDoc) is NotFoundResult,"Unrelated doctor request access was not denied.");
            Require(await Invoke(c=>c.Create(new LabRequestForm{MedicalRecordId=encounter.Id,LabTestId=numeric.Id,CreationToken=Guid.NewGuid()}),otherDoc) is NotFoundResult,"Unrelated doctor ordering was not denied.");
            Require(await Invoke(c=>c.Progress(id,initial.Version,"Process"),lab,true) is BadRequestObjectResult,"Processing before collection was not rejected.");
            await Progress(id,"Collect");Require(await Invoke(c=>c.Progress(id,initial.Version,"Collect"),lab,true) is ConflictObjectResult,"Repeated stale sample collection was accepted.");
            var collected=await Request(id);Require(collected.SampleIdentifier?.StartsWith("LAB-")==true&&collected.CollectedByUserId==lab,"Sample identity/custody missing.");
            Require(await Invoke(c=>c.Progress(id,collected.Version,"Process"),lab,true) is BadRequestObjectResult,"Processing before receipt was accepted.");
            await Progress(id,"Receive");await Progress(id,"Process");
            var entry=new LabResultForm{LabRequestId=id,CreationToken=Guid.NewGuid(),NumericValue=123.4567m,Comments="Synthetic rollback comment"};
            Require(await Invoke(c=>c.EnterResult(entry),lab,true) is RedirectToActionResult,"Numeric result entry failed.");Require(await Invoke(c=>c.EnterResult(entry),lab,true) is RedirectToActionResult,"Result draft idempotency failed.");await Flush();
            var first=await Result(id);
            Require(await Invoke(c=>c.EditResult(first.Id),otherLab,true) is NotFoundResult,"Another staff member accessed an editable draft.");
            Require(await Invoke(c=>c.EditResult(first.Id,new LabResultForm{Version=first.Version,NumericValue=1}),otherLab,true) is NotFoundResult,"Another staff member edited a draft.");
            Require(await Invoke(c=>c.FinalizeResult(first.Id,first.Version),otherLab,true) is NotFoundResult,"Another staff member finalized a draft.");
            Require(await Invoke(c=>c.Report(first.Id)) is NotFoundResult,"Doctor accessed draft report.");
            Require((await Invoke(c=>c.Details(id)) as ViewResult)?.Model is LabRequestDetails doctorDraft && doctorDraft.Results.Count==0,"Doctor accessed private draft values.");
            Require(await Invoke(c=>c.FinalizeResult(first.Id,first.Version),lab,true) is RedirectToActionResult,"Numeric finalization failed.");await Flush();first=await Result(id);
            var completed=await Request(id);Require(completed.Status==LabRequestStatus.Completed&&first.FinalizedAtUtc!=null,"Request/result completion missing.");
            Require(await Invoke(c=>c.Report(first.Id)) is ViewResult&&await Invoke(c=>c.Report(first.Id,true)) is FileContentResult,"Authorized report/PDF failed.");
            Require(await Invoke(c=>c.Report(first.Id,true),otherDoc) is NotFoundResult,"Unrelated doctor exported a report.");
            Require(await Invoke(c=>c.Correct(first.Id,first.Version,"Synthetic correction"),otherLab,true) is NotFoundResult,"Another staff member started a correction.");
            Require(await Invoke(c=>c.Correct(first.Id,first.Version,"Synthetic correction reason"),lab,true) is RedirectToActionResult,"Correction setup failed.");await Flush();
            Require(await Invoke(c=>c.Correct(first.Id,first.Version,"Synthetic duplicate"),lab,true) is ConflictObjectResult,"Duplicate correction accepted.");
            var correction=await Result(id,2);Require(await Invoke(c=>c.EditResult(correction.Id,new LabResultForm{Version=correction.Version,NumericValue=234.5678m,CorrectionReason="Synthetic correction reason"}),lab,true) is RedirectToActionResult,"Correction edit failed.");
            correction=await Result(id,2);Require(await Invoke(c=>c.Report(correction.Id)) is NotFoundResult,"Doctor accessed unfinalized correction.");
            Require(await Invoke(c=>c.FinalizeResult(correction.Id,correction.Version),lab,true) is RedirectToActionResult,"Correction finalization failed.");await Flush();
            var old=await Result(id);Require(old.NumericValue==first.NumericValue&&old.FinalizedAtUtc==first.FinalizedAtUtc&&(await Request(id)).CompletedAtUtc==completed.CompletedAtUtc,"Finalized history or original completion time changed.");
            Console.WriteLine("PostgreSQL PASS: request/draft idempotency; ordered collection/receipt/processing; stale action rejection; numeric entry/finalization; author-owned correction; private draft and unrelated-doctor access restrictions; reports/PDF; prior history retained.");

            var textId=await Create(text.Id);foreach(var command in new[]{"Collect","Receive","Process"})await Progress(textId,command);
            Require(await Invoke(c=>c.EnterResult(new LabResultForm{LabRequestId=textId,CreationToken=Guid.NewGuid(),TextValue="Synthetic rollback text"}),lab,true) is RedirectToActionResult,"Text result entry failed.");
            var textResult=await Result(textId);Require(await Invoke(c=>c.FinalizeResult(textResult.Id,textResult.Version),lab,true) is RedirectToActionResult,"Text finalization failed.");await Flush();
            var cancelledId=await Create(numeric.Id);await Progress(cancelledId,"Collect");var cancel=await Request(cancelledId);
            Require(await Invoke(c=>c.Cancel(cancelledId,cancel.Version,"Synthetic cancellation")) is ForbidResult,"Doctor cancelled a collected request.");
            Require(await Invoke(c=>c.Cancel(cancelledId,cancel.Version,"Synthetic cancellation"),lab,true) is RedirectToActionResult,"Staff cancellation failed.");await Flush();
            Require((await Request(cancelledId)).SampleIdentifier==cancel.SampleIdentifier,"Cancellation erased sample history.");
            Console.WriteLine("PostgreSQL PASS: text results and finalization; cancellation permissions and retained sample history; deferred consistency checks explicitly flushed.");

            async Task Reject(string label,string sql,string state,params (string,object)[] parameters)
            {
                await Flush();await tx.SaveAsync("lab_probe");
                try
                {
                    await using var cmd=new NpgsqlCommand(sql,connection,tx);foreach(var p in parameters)cmd.Parameters.AddWithValue(p.Item1,p.Item2);await cmd.ExecuteNonQueryAsync();
                    await Flush();throw new CheckFailure("Database guard did not reject: "+label);
                }
                catch(PostgresException ex) when(ex.SqlState==state)
                {
                    if(label.Contains("(deferred)"))Require(ex.Where?.Contains("medicare_lab_completion_check")==true,"Expected the deferred completion trigger to reject the inconsistent state.");
                    Console.WriteLine("PostgreSQL guard PASS: "+label+"; SQLSTATE="+state);
                }
                finally{await tx.RollbackAsync("lab_probe");await tx.ReleaseAsync("lab_probe");}
            }
            await Reject("finalized result update","UPDATE lab_result_revisions SET \"NumericValue\"=1 WHERE \"Id\"=@id","23514",("id",first.Id));
            await Reject("finalized result deletion","DELETE FROM lab_result_revisions WHERE \"Id\"=@id","23514",("id",first.Id));
            await Reject("request identity/snapshot change","UPDATE lab_requests SET \"TestName\"='Synthetic forbidden snapshot' WHERE \"Id\"=@id","23514",("id",id));
            await Reject("terminal request reopening","UPDATE lab_requests SET \"Status\"='Requested' WHERE \"Id\"=@id","23514",("id",id));
            await Reject("sample custody overwrite","UPDATE lab_requests SET \"CollectedByUserId\"=@staff WHERE \"Id\"=@id","23514",("staff",otherLab),("id",cancelledId));
            var probeId=await Create(numeric.Id);var probe=await Request(probeId);
            await Reject("duplicate request token","INSERT INTO lab_requests SELECT (jsonb_populate_record(NULL::lab_requests,to_jsonb(q)||jsonb_build_object('Id',@newid::text))).* FROM lab_requests q WHERE q.\"Id\"=@id","23505",("newid",Guid.NewGuid()),("id",probeId));
            await Reject("duplicate sample identifier","UPDATE lab_requests SET \"Status\"='SampleCollected',\"SampleIdentifier\"=@sample,\"CollectedAtUtc\"=clock_timestamp(),\"CollectedByUserId\"=@staff WHERE \"Id\"=@id","23505",("sample",cancel.SampleIdentifier!),("staff",lab),("id",probeId));
            await Reject("invalid status jump","UPDATE lab_requests SET \"Status\"='InProgress' WHERE \"Id\"=@id","23514",("id",probeId));
            foreach(var command in new[]{"Collect","Receive","Process"})await Progress(probeId,command);
            await Reject("completion without finalized result (deferred)","UPDATE lab_requests SET \"Status\"='Completed',\"CompletedAtUtc\"=clock_timestamp() WHERE \"Id\"=@id","23514",("id",probeId));
            Require(await Invoke(c=>c.EnterResult(new LabResultForm{LabRequestId=probeId,CreationToken=Guid.NewGuid(),NumericValue=1}),lab,true) is RedirectToActionResult,"Concurrency draft setup failed.");await Flush();var draft=await Result(probeId);
            await Reject("finalized result without Completed request (deferred)","UPDATE lab_result_revisions SET \"FinalizedAtUtc\"=clock_timestamp() WHERE \"Id\"=@id","23514",("id",draft.Id));
            await Reject("wrong result format","UPDATE lab_result_revisions SET \"NumericValue\"=NULL,\"TextValue\"='Synthetic wrong format' WHERE \"Id\"=@id","23514",("id",draft.Id));
            await Reject("result author change","UPDATE lab_result_revisions SET \"AuthorUserId\"=@author WHERE \"Id\"=@id","23514",("author",otherLab),("id",draft.Id));
            await Reject("nonfinite numeric result","UPDATE lab_result_revisions SET \"NumericValue\"='NaN'::numeric WHERE \"Id\"=@id","23514",("id",draft.Id));
            await Reject("duplicate draft/sequential revision","INSERT INTO lab_result_revisions SELECT (jsonb_populate_record(NULL::lab_result_revisions,to_jsonb(v)||jsonb_build_object('Id',@newid::text,'CreationToken',@token::text,'Number',2,'CorrectionReason','Synthetic'))).* FROM lab_result_revisions v WHERE v.\"Id\"=@id","23514",("newid",Guid.NewGuid()),("token",Guid.NewGuid()),("id",draft.Id));

            await using(var a=Context(connection))await using(var b=Context(connection))
            {
                await a.Database.UseTransactionAsync(tx);await b.Database.UseTransactionAsync(tx);
                var winner=await a.LabResultRevisions.SingleAsync(r=>r.Id==draft.Id);var stale=await b.LabResultRevisions.SingleAsync(r=>r.Id==draft.Id);
                winner.NumericValue=2;winner.Version=Guid.NewGuid();await a.SaveChangesAsync();await Flush();var beforeFailure=await Snapshot(connection);
                stale.NumericValue=3;stale.Version=Guid.NewGuid();b.SecurityAuditLogs.Add(new SecurityAuditLog{ActorUserId=lab,TargetUserId=draft.Id.ToString(),Action="SyntheticStaleLabWrite",Succeeded=true});
                try{await b.SaveChangesAsync();throw new CheckFailure("Stale PostgreSQL result write accepted.");}catch(DbUpdateConcurrencyException){Console.WriteLine("PostgreSQL PASS: actual stale EF result write rejected.");}
                await Flush();await Preserved(connection,beforeFailure);
            }
            await using(var a=Context(connection))await using(var b=Context(connection))
            {
                await a.Database.UseTransactionAsync(tx);await b.Database.UseTransactionAsync(tx);var winner=await a.LabRequests.SingleAsync(r=>r.Id==probeId);var stale=await b.LabRequests.SingleAsync(r=>r.Id==probeId);
                winner.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();
                try{await b.SaveChangesAsync();throw new CheckFailure("Stale PostgreSQL request write accepted.");}catch(DbUpdateConcurrencyException){Console.WriteLine("PostgreSQL PASS: actual stale EF request write rejected.");}await Flush();
            }
            await using(var db=Context(connection))
            {
                await db.Database.UseTransactionAsync(tx);Require(await db.Appointments.Where(a=>a.Id==appointment.Id).Select(a=>a.Status).SingleAsync()==AppointmentStatus.Scheduled,"Laboratory changed appointment status.");
                Require(!await db.MedicalRecordRevisions.AnyAsync(r=>r.MedicalRecordId==encounter.Id&&r.FinalizedAtUtc!=null),"Laboratory finalized an unrelated EMR encounter.");
            }
            Console.WriteLine("PostgreSQL PASS: all workflow/trigger probes flushed deferred checks; stale result/request conflicts and atomic failed audit rollback; appointment and EMR statuses preserved. MVC role/antiforgery filters are verified separately by HTTP tests.");
        }
        finally{await tx.RollbackAsync();Console.WriteLine("Rolled back all synthetic laboratory, clinical, Identity, token and audit fixtures.");}
    }
    private sealed class CheckFailure(string message):Exception(message);
}
