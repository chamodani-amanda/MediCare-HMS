using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Npgsql;

const string target = "20261119030236_AddAppointmentManagement";
const string previous = "20261118184537_AddDoctorManagement";
var stage = "target verification";
try
{
    var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web"))
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true)
        .AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
    var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));
    Require(cs.Database == "medicare_dev", "Configured target mismatch; connection refused."); cs.Timeout=10;
    await using var connection = new NpgsqlConnection(cs.ConnectionString); await connection.OpenAsync();
    await using (var cmd = new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'",connection))
        Require((bool?)await cmd.ExecuteScalarAsync()==true,"Actual database/schema mismatch.");
    Console.WriteLine("Confirmed connected database: medicare_dev; target schema: public.");
    var before = await Snapshot(connection);
    Require(before.ContainsKey("patients") && before.ContainsKey("doctors") && before.Keys.Count(k=>k.StartsWith("AspNet",StringComparison.Ordinal))==7,"Existing application tables missing.");
    await using var db = Context(connection);
    if (args.Contains("--apply-approved"))
    {
        stage="backup verification";
        const string backup=@"D:\MediCare-Backups\medicare_dev_before_step7_20261119_025724_23678072.backup";
        await using (var stream=File.OpenRead(backup))
            Require(stream.Length==32257 && Convert.ToHexString(await SHA256.HashDataAsync(stream))=="F4EB7B156486DCCE0207D99BF8D6340C3B7BBDBFF1CD9FCDE5716414179E5E10","Backup size/checksum mismatch.");
        var listing=await Archive("--list",backup);
        Require(listing.Contains("dbname: medicare_dev") && listing.Contains("TABLE DATA"),"Unexpected archive contents."); await Archive("--file=NUL",backup);
        Console.WriteLine("Backup PASS: 32,257 bytes, unchanged SHA-256; archive listing and full offline read passed. No restore performed.");
        stage="extension and migration safety";
        await using (var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_available_extensions WHERE name='btree_gist') AND (EXISTS(SELECT 1 FROM pg_extension WHERE extname='btree_gist') OR has_database_privilege(current_user,current_database(),'CREATE'))",connection))
            Require((bool?)await cmd.ExecuteScalarAsync()==true,"Extension unavailable or insufficient privileges.");
        var assembly=db.GetService<IMigrationsAssembly>(); var migration=assembly.CreateMigration(assembly.Migrations[target],db.Database.ProviderName!);
        Require(migration.UpOperations.Count==7 && migration.UpOperations.All(op=>op is AlterDatabaseOperation or CreateTableOperation or CreateIndexOperation or SqlOperation),"Unexpected migration operations.");
        var table=migration.UpOperations.OfType<CreateTableOperation>().Single();
        Require(table.Name=="appointments" && table.ForeignKeys.Count==2 && table.ForeignKeys.All(f=>f.OnDelete==ReferentialAction.Restrict) && table.CheckConstraints.Count==5,"Unexpected appointment table definition.");
        var alteration=migration.UpOperations.OfType<AlterDatabaseOperation>().Single();
        Require(alteration.GetAnnotations().Count()==1 && alteration.FindAnnotation("Npgsql:PostgresExtension:btree_gist")!=null,"Unexpected database alteration.");
        Require(migration.UpOperations.OfType<CreateIndexOperation>().All(i=>i.Table=="appointments"),"Unexpected index target.");
        var exclusion=migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        Require(exclusion.Contains("EXCLUDE USING gist") && exclusion.Contains("tstzrange") && exclusion.Contains("'[)'") && exclusion.Contains("Cancelled"),"Unexpected exclusion SQL.");
        var migrator=db.GetService<IMigrator>();
        string Normalize(string value)=>value.Replace("\r\n","\n").Trim();
        Require(Normalize(migrator.GenerateScript(previous,target))==Normalize(await File.ReadAllTextAsync("tools/AppointmentManagement-migration.sql")),"Generated SQL differs from reviewed additive script.");
        Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{target}),"Unexpected pending migration set; stopped before application.");
        Console.WriteLine("Preflight PASS: extension available; only approved additive operations; SQL matches reviewed script; only approved migration pending.");
        stage="approved migration application"; await migrator.MigrateAsync(target); Console.WriteLine("Applied migration: "+target);
    }
    stage="schema verification";
    Require((await db.Database.GetAppliedMigrationsAsync()).Contains(target) && !(await db.Database.GetPendingMigrationsAsync()).Any(),"Unexpected migration history.");
    await Schema(connection); await Preserved(connection,before);
    if(args.Contains("--integration"))
    {
        stage="rollback-only integration"; await Integration(connection,cs.ConnectionString); await Preserved(connection,before);
    }
    Console.WriteLine("SUCCESS: appointment schema verified; all original rows preserved; no pending migrations.");
}
catch(Exception ex)
{
    Console.WriteLine("STOPPED during "+stage+": "+ex.GetType().Name+(ex is CheckFailure ? "; "+ex.Message : ex is PostgresException pg ? "; SQLSTATE="+pg.SqlState : "")+". Provider details suppressed to protect secrets.");
    Environment.ExitCode=1;
}

static void Require(bool condition,string message){if(!condition)throw new CheckFailure(message);}
static MediCareDbContext Context(NpgsqlConnection connection)=>new(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql(connection).Options);
static T Actor<T>(T controller,string role="Administrator",string user="rollback-integration") where T:Controller
{
    controller.ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Role,role)},"Integration"))}};
    return controller;
}
static async Task<string> Archive(string option,string path)
{
    var info=new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
    info.ArgumentList.Add(option); info.ArgumentList.Add(path); using var p=Process.Start(info)??throw new CheckFailure("Cannot start pg_restore.");
    var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();var text=await output;await error;Require(p.ExitCode==0,"Archive unreadable.");return text;
}
static async Task Schema(NpgsqlConnection connection)
{
    var checks=new Dictionary<string,string>
    {
        ["btree_gist installed"]="SELECT EXISTS(SELECT 1 FROM pg_extension WHERE extname='btree_gist')",
        ["appointment columns"]="SELECT count(*)=13 FROM information_schema.columns WHERE table_schema='public' AND table_name='appointments'",
        ["primary key"]="SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.appointments'::regclass AND contype='p' AND convalidated)",
        ["restrictive doctor/patient foreign keys"]="SELECT count(*)=2 FROM pg_constraint WHERE conrelid='public.appointments'::regclass AND contype='f' AND confdeltype='r' AND convalidated AND ((conname='FK_appointments_doctors_DoctorId' AND confrelid='public.doctors'::regclass) OR (conname='FK_appointments_patients_PatientId' AND confrelid='public.patients'::regclass))",
        ["five validated checks"]="SELECT count(*)=5 FROM pg_constraint WHERE conrelid='public.appointments'::regclass AND contype='c' AND convalidated",
        ["doctor/time exclusion"]="SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.appointments'::regclass AND conname='EX_appointments_doctor_time' AND contype='x' AND convalidated AND pg_get_constraintdef(oid) LIKE '%tstzrange%' AND pg_get_constraintdef(oid) LIKE '%Cancelled%')",
        ["four B-tree indexes and GiST exclusion index"]="SELECT count(*)=5 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE i.indrelid='public.appointments'::regclass AND i.indisvalid AND c.relname IN ('IX_appointments_BookingToken','IX_appointments_DoctorId_StartAtUtc','IX_appointments_PatientId_StartAtUtc','IX_appointments_StartAtUtc_Status','EX_appointments_doctor_time')",
        ["unique booking token"]="SELECT EXISTS(SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE i.indrelid='public.appointments'::regclass AND c.relname='IX_appointments_BookingToken' AND i.indisunique AND i.indisvalid)",
        ["uuid concurrency column"]="SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='appointments' AND column_name='Version' AND udt_name='uuid' AND is_nullable='NO')"
    };
    foreach(var check in checks){await using var cmd=new NpgsqlCommand(check.Value,connection);Require((bool?)await cmd.ExecuteScalarAsync()==true,"Schema mismatch: "+check.Key);Console.WriteLine("Schema PASS: "+check.Key);}
}
static async Task<Dictionary<string,(long Count,string Hash)>> Snapshot(NpgsqlConnection connection)
{
    var names=new List<string>();await using(var cmd=new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename<>'__EFMigrationsHistory' ORDER BY tablename",connection))
    await using(var reader=await cmd.ExecuteReaderAsync())while(await reader.ReadAsync())names.Add(reader.GetString(0));
    var result=new Dictionary<string,(long,string)>();foreach(var name in names){using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long count=0;
        await using var cmd=new NpgsqlCommand("SELECT row_to_json(t)::text FROM public.\""+name.Replace("\"","\"\"")+"\" t ORDER BY row_to_json(t)::text COLLATE \"C\"",connection);
        await using var reader=await cmd.ExecuteReaderAsync();while(await reader.ReadAsync()){hash.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0)+"\n"));count++;}result[name]=(count,Convert.ToHexString(hash.GetHashAndReset()));}return result;
}
static async Task Preserved(NpgsqlConnection connection,Dictionary<string,(long Count,string Hash)> before)
{
    var after=await Snapshot(connection);foreach(var pair in before){Require(after.TryGetValue(pair.Key,out var value)&&value==pair.Value,"Existing rows changed: "+pair.Key);Console.WriteLine("Preserved: "+pair.Key+"; rows="+pair.Value.Count+"; full-row fingerprints unchanged.");}
}

static async Task Integration(NpgsqlConnection connection,string cs)
{
    await using var tx=await connection.BeginTransactionAsync();
    try
    {
        await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);
        var clock=new CheckClock();var date=HospitalTime.Today(clock).AddDays(7);while(date.DayOfWeek!=DayOfWeek.Monday)date=date.AddDays(1);
        var prefix="APPT-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var department=new Department{Name=prefix,NormalizedName=prefix};var patient=new Patient{FirstName="Integration",LastName="Patient",PatientNumber=prefix};
        var doctor=new Doctor{FirstName="Integration",LastName="Doctor",RegistrationNumber=prefix,DepartmentId=department.Id,Qualifications="Test",Specialization="Test",IdentityUserId=null};
        db.AddRange(department,patient,doctor,new DoctorSchedule{DoctorId=doctor.Id,DayOfWeek=date.DayOfWeek,StartTime=new TimeOnly(9,0),EndTime=new TimeOnly(17,0)});await db.SaveChangesAsync();
        AppointmentFormViewModel Fields(string time="09:00",int duration=30)=>new(){BookingToken=Guid.NewGuid(),PatientId=patient.Id,DoctorId=doctor.Id,Date=date,StartTime=TimeOnly.Parse(time),DurationMinutes=duration};
        AppointmentsController Controller()=>Actor(new AppointmentsController(db,clock));
        var fields=Fields();Require(await Controller().Create(fields) is RedirectToActionResult,"Valid booking failed.");var booked=await db.Appointments.SingleAsync(a=>a.BookingToken==fields.BookingToken);
        Require(await Controller().Create(fields) is RedirectToActionResult && await db.Appointments.CountAsync(a=>a.DoctorId==doctor.Id)==1,"Repeat submission was not idempotent.");
        var overlap=Controller();Require(await overlap.Create(Fields("09:15",60)) is ViewResult&&!overlap.ModelState.IsValid,"Overlap accepted by application.");
        var outside=Controller();Require(await outside.Create(Fields("08:45")) is ViewResult&&!outside.ModelState.IsValid,"Outside-hours booking accepted.");
        Require(await Controller().Create(Fields("09:30",15)) is RedirectToActionResult,"Adjacent appointment rejected.");
        Console.WriteLine("Integration PASS: real PostgreSQL booking, duplicate-submit idempotency, overlap/duration/working-hour checks and adjacency.");

        await ExpectSql(connection,tx,"UPDATE appointments SET \"StartAtUtc\"=\"StartAtUtc\"+INTERVAL '15 minutes', \"EndAtUtc\"=\"EndAtUtc\"+INTERVAL '15 minutes' WHERE \"Id\"=@id",booked.Id,"23P01");
        await ExpectSql(connection,tx,"UPDATE appointments SET \"DurationMinutes\"=20, \"EndAtUtc\"=\"StartAtUtc\"+INTERVAL '20 minutes' WHERE \"Id\"=@id",booked.Id,"23514");
        await ExpectSql(connection,tx,"UPDATE appointments SET \"StartAtUtc\"=\"StartAtUtc\"+INTERVAL '1 minute', \"EndAtUtc\"=\"EndAtUtc\"+INTERVAL '1 minute' WHERE \"Id\"=@id",booked.Id,"23514");
        await ExpectSql(connection,tx,"UPDATE appointments SET \"Status\"='Invalid' WHERE \"Id\"=@id",booked.Id,"23514");
        await ExpectSql(connection,tx,"UPDATE appointments SET \"PatientId\"='00000000-0000-0000-0000-000000000000'::uuid WHERE \"Id\"=@id",booked.Id,"23503");
        var adjacentId=await db.Appointments.Where(a=>a.DoctorId==doctor.Id&&a.Id!=booked.Id).Select(a=>a.Id).SingleAsync();
        await ExpectSql(connection,tx,"UPDATE appointments SET \"BookingToken\"=(SELECT \"BookingToken\" FROM appointments WHERE \"Id\"='"+booked.Id+"'::uuid) WHERE \"Id\"=@id",adjacentId,"23505");
        Console.WriteLine("Integration PASS: PostgreSQL exclusion, duration, interval, status and relationship constraints reject invalid writes.");

        var move=Fields("10:00",45);move.Id=booked.Id;move.Version=booked.Version;
        Require(await Controller().Reschedule(move) is RedirectToActionResult,"Rescheduling failed.");
        var stale=Controller();Require(await stale.Reschedule(move) is ViewResult&&!stale.ModelState.IsValid,"Stale reschedule accepted.");
        Require(await Controller().ChangeStatus(new AppointmentChangeViewModel{Id=booked.Id,Version=booked.Version,Status=AppointmentStatus.Cancelled,CancellationReason="Integration"}) is RedirectToActionResult,"Cancellation failed.");
        Require(booked.Status==AppointmentStatus.Cancelled&&booked.CancelledAtUtc!=null,"Cancelled history missing.");
        var terminal=Controller();Require(await terminal.Reschedule(new AppointmentFormViewModel{Id=booked.Id,Version=booked.Version,PatientId=patient.Id,DoctorId=doctor.Id,Date=date,StartTime=new TimeOnly(11,0),DurationMinutes=30}) is ViewResult&&!terminal.ModelState.IsValid,"Terminal appointment rescheduled.");
        Require(await Controller().Create(Fields("10:00",45)) is RedirectToActionResult,"Cancelled slot could not be reused.");
        var completed=await db.Appointments.SingleAsync(a=>a.DoctorId==doctor.Id&&a.Status==AppointmentStatus.Scheduled&&a.StartAtUtc==HospitalTime.ToUtc(date,new TimeOnly(10,0)));
        await using(var competing=Context(connection))
        {
            await competing.Database.UseTransactionAsync(tx);var staleRow=await competing.Appointments.SingleAsync(a=>a.Id==completed.Id);
            var original=staleRow.Version;var firstMove=Fields("11:00");firstMove.Id=completed.Id;firstMove.Version=completed.Version;
            Require(await Controller().Reschedule(firstMove) is RedirectToActionResult,"First concurrency writer failed.");
            var count=await db.SecurityAuditLogs.CountAsync();var staleMove=Fields("12:00");staleMove.Id=completed.Id;staleMove.Version=original;
            var writer=Actor(new AppointmentsController(competing,clock));
            Require(await writer.Reschedule(staleMove) is ViewResult&&writer.ModelState.Values.SelectMany(v=>v.Errors).Any(e=>e.ErrorMessage.Contains("while saving")),"PostgreSQL optimistic update did not reject stale row version.");
            Require(await db.SecurityAuditLogs.CountAsync()==count&&(await db.Appointments.AsNoTracking().SingleAsync(a=>a.Id==completed.Id)).StartAtUtc==HospitalTime.ToUtc(date,new TimeOnly(11,0)),"Failed concurrency writer left changes or audit rows behind.");
        }
        Console.WriteLine("Integration PASS: PostgreSQL row-version concurrency rejects stale saves and atomically rolls back their appointment/audit changes.");
        var early=Controller();Require(await early.ChangeStatus(new AppointmentChangeViewModel{Id=completed.Id,Version=completed.Version,Status=AppointmentStatus.Completed}) is ViewResult&&!early.ModelState.IsValid,"Early completion accepted.");
        clock.Now=new DateTimeOffset(completed.EndAtUtc.AddMinutes(1));Require(await Controller().ChangeStatus(new AppointmentChangeViewModel{Id=completed.Id,Version=completed.Version,Status=AppointmentStatus.Completed}) is RedirectToActionResult,"Completion after end failed.");
        var noShow=await db.Appointments.SingleAsync(a=>a.DoctorId==doctor.Id&&a.Status==AppointmentStatus.Scheduled);
        Require(await Controller().ChangeStatus(new AppointmentChangeViewModel{Id=noShow.Id,Version=noShow.Version,Status=AppointmentStatus.NoShow}) is RedirectToActionResult,"NoShow after end failed.");
        Console.WriteLine("Integration PASS: rescheduling, stale versions, cancellation/history/slot reuse, terminal rules, Completed and NoShow boundaries.");
        var list=await Controller().Index(prefix,null,date,null);Require(list is ViewResult,"PostgreSQL list/search query failed.");
        Require(await Controller().Calendar(date,doctor.Id) is ViewResult,"PostgreSQL calendar query failed.");
        Require(await Controller().Availability(doctor.Id,date,30) is ViewResult,"PostgreSQL availability query failed.");
        var privateController=Actor(new AppointmentsController(db,clock),"Doctor","unlinked-integration");Require(await privateController.Details(booked.Id) is NotFoundResult,"Unlinked Doctor saw patient information.");
        Console.WriteLine("Integration PASS: PostgreSQL listing, calendar, availability and unlinked Doctor privacy.");
        await Race(connection,cs,patient.Id,doctor.Id);
    }
    finally{await tx.RollbackAsync();Console.WriteLine("Integration rolled back: no fixture patients/doctors/departments/schedules/appointments/audit records retained.");}
}
static async Task ExpectSql(NpgsqlConnection connection,NpgsqlTransaction tx,string sql,Guid id,string code)
{
    await tx.SaveAsync("constraint_probe");var rejected=false;try{await using var cmd=new NpgsqlCommand(sql,connection,tx);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();}
    catch(PostgresException ex)when(ex.SqlState==code){rejected=true;}finally{await tx.RollbackAsync("constraint_probe");await tx.ReleaseAsync("constraint_probe");}
    Require(rejected,"Expected PostgreSQL constraint rejection "+code+" was not observed.");
}

static async Task Race(NpgsqlConnection baseline,string cs,Guid fixturePatient,Guid fixtureDoctor)
{
    await using var a=new NpgsqlConnection(cs);await using var b=new NpgsqlConnection(cs);await a.OpenAsync();await b.OpenAsync();
    Guid patientId=fixturePatient,doctorId=fixtureDoctor;var pendingParents=true;
    // The independent connection sees committed parents only, not the outer fixtures.
    await using(var cmd=new NpgsqlCommand("SELECT (SELECT \"Id\" FROM patients ORDER BY \"Id\" LIMIT 1),(SELECT \"Id\" FROM doctors ORDER BY \"Id\" LIMIT 1)",a))
    await using(var reader=await cmd.ExecuteReaderAsync())
        if(await reader.ReadAsync()&&!reader.IsDBNull(0)&&!reader.IsDBNull(1)){patientId=reader.GetGuid(0);doctorId=reader.GetGuid(1);pendingParents=false;}
    if(pendingParents)
    {
        Console.WriteLine("Two-session SKIPPED: no committed doctor/patient pair exists. No fake clinical records will be committed. Register a real doctor, then repeat --integration.");
        return;
    }
    var date=DateTime.UtcNow.Date.AddYears(20);var start=DateTime.SpecifyKind(date.AddHours(3),DateTimeKind.Utc);
    // Find a far-future empty interval without changing any existing appointment.
    await using(var cmd=new NpgsqlCommand("SELECT count(*) FROM appointments WHERE \"DoctorId\"=@doctor AND \"Status\"<>'Cancelled' AND \"StartAtUtc\"<@until AND \"EndAtUtc\">@from",baseline))
    {cmd.Parameters.AddWithValue("doctor",doctorId);cmd.Parameters.AddWithValue("from",start);cmd.Parameters.AddWithValue("until",start.AddMinutes(60));Require((long)(await cmd.ExecuteScalarAsync())! ==0,"Selected concurrency probe interval is occupied.");}
    await using var txA=await a.BeginTransactionAsync();await using var txB=await b.BeginTransactionAsync();
    await using var first=Insert(a,txA,patientId,doctorId,start);await using var second=Insert(b,txB,patientId,doctorId,start.AddMinutes(15));
    Task<int>? firstTask=null,secondTask=null;
    try
    {
        firstTask=first.ExecuteNonQueryAsync();
        await firstTask;
        secondTask=second.ExecuteNonQueryAsync();
        Require(await WaitForBlock(baseline,a.ProcessID,b.ProcessID,secondTask),"Second overlapping INSERT did not wait on the first reservation.");
        Console.WriteLine("Two-session PASS: overlapping INSERT on connection B is blocked specifically by connection A's exclusion reservation.");
        await txA.RollbackAsync();await secondTask.WaitAsync(TimeSpan.FromSeconds(10));await txB.RollbackAsync();
        Console.WriteLine("Two-session PASS: rolling back A releases B; both test reservations rolled back.");
    }
    finally
    {
        if(firstTask is { IsCompleted:false })first.Cancel();
        if(firstTask!=null){try{await firstTask;}catch(Exception ex)when(ex is NpgsqlException or OperationCanceledException){}}
        if(txA.Connection!=null)await txA.RollbackAsync();
        if(secondTask is { IsCompleted:false })second.Cancel();
        if(secondTask!=null){try{await secondTask;}catch(Exception ex)when(ex is NpgsqlException or OperationCanceledException){}}
        if(txB.Connection!=null)await txB.RollbackAsync();
        Console.WriteLine("Parallel transactions cancelled/rolled back; no concurrency-test appointment retained.");
    }
}
static async Task<bool> WaitForBlock(NpgsqlConnection monitor,int blocker,int waiter,Task command)
{
    for(var attempt=0;attempt<100&&!command.IsCompleted;attempt++)
    {
        await using var query=new NpgsqlCommand("SELECT @blocker=ANY(pg_blocking_pids(@waiter))",monitor);
        query.Parameters.AddWithValue("blocker",blocker);query.Parameters.AddWithValue("waiter",waiter);
        if((bool?)await query.ExecuteScalarAsync()==true)return true;await Task.Delay(50);
    }
    return false;
}
static NpgsqlCommand Insert(NpgsqlConnection connection,NpgsqlTransaction tx,Guid patient,Guid doctor,DateTime start)
{
    var cmd=new NpgsqlCommand("INSERT INTO appointments (\"Id\",\"BookingToken\",\"PatientId\",\"DoctorId\",\"StartAtUtc\",\"EndAtUtc\",\"DurationMinutes\",\"Status\",\"Version\",\"CreatedAtUtc\",\"UpdatedAtUtc\") VALUES (@id,@token,@patient,@doctor,@start,@end,30,'Scheduled',@version,@now,@now)",connection,tx);
    cmd.CommandTimeout=15;cmd.Parameters.AddWithValue("id",Guid.NewGuid());cmd.Parameters.AddWithValue("token",Guid.NewGuid());cmd.Parameters.AddWithValue("patient",patient);cmd.Parameters.AddWithValue("doctor",doctor);
    cmd.Parameters.AddWithValue("start",start);cmd.Parameters.AddWithValue("end",start.AddMinutes(30));cmd.Parameters.AddWithValue("version",Guid.NewGuid());cmd.Parameters.AddWithValue("now",DateTime.UtcNow);return cmd;
}
sealed class CheckClock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
sealed class CheckFailure(string message):Exception(message);
