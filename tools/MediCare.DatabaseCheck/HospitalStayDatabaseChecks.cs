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

internal static class HospitalStayDatabaseChecks
{
    // These commands refuse to run until the independently approved migration is applied.
    internal static async Task Run(string[] args)
    {
        try
        {
            var config=new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
            var cs=new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));Require(cs.Database=="medicare_dev");cs.Timeout=10;
            await using var c=new NpgsqlConnection(cs.ConnectionString);await c.OpenAsync();await using(var cmd=new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
            Console.WriteLine("Confirmed actual database: medicare_dev.");
            var before=await Snapshot(c);
            if(args.Contains("--apply-approved-stay"))
            {
                await using(var tx=await c.BeginTransactionAsync())
                {
                    await using(var cmd=new NpgsqlCommand("SET TRANSACTION READ ONLY",c,tx))await cmd.ExecuteNonQueryAsync();
                    await using var preflight=Context(c);await preflight.Database.UseTransactionAsync(tx);
                    await HospitalStayPreflight.Verify(preflight,c,tx);await tx.RollbackAsync();
                }
                await using var application=Context(c);
                Require((await application.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{HospitalStayPreflight.Target}));
                await application.GetService<IMigrator>().MigrateAsync(HospitalStayPreflight.Target);
                Console.WriteLine("Approved hospital stay migration applied to medicare_dev.");
            }
            await using var db=Context(c);Require(!(await db.Database.GetPendingMigrationsAsync()).Any());Require((await db.Database.GetAppliedMigrationsAsync()).Contains(HospitalStayPreflight.Target));
            await Schema(db,c);if(args.Contains("--integration-stay"))await Integration(c);await Preserved(c,before);Console.WriteLine("Hospital stay verification passed; original records preserved.");
        }
        catch(Exception ex){var pg=ex as PostgresException??ex.InnerException as PostgresException;Console.WriteLine("Stay verification STOPPED: "+ex.GetType().Name+(pg==null?"":"; SQLSTATE="+pg.SqlState+"; constraint="+pg.ConstraintName)+". Provider details suppressed.");Environment.ExitCode=1;}
    }
    private static async Task Schema(MediCareDbContext db,NpgsqlConnection c)
    {
        var a=db.GetService<IMigrationsAssembly>();var m=a.CreateMigration(a.Migrations[HospitalStayPreflight.Target],db.Database.ProviderName!);
        foreach(var t in m.UpOperations.OfType<CreateTableOperation>())
        {
            foreach(var column in t.Columns){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column AND is_nullable=@nullable",c);cmd.Parameters.AddWithValue("table",t.Name);cmd.Parameters.AddWithValue("column",column.Name);cmd.Parameters.AddWithValue("nullable",column.IsNullable?"YES":"NO");Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var fk in t.ForeignKeys){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND confdeltype='r' AND convalidated",c);cmd.Parameters.AddWithValue("table",t.Name);cmd.Parameters.AddWithValue("name",fk.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var check in t.CheckConstraints){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND contype='c' AND convalidated",c);cmd.Parameters.AddWithValue("table",t.Name);cmd.Parameters.AddWithValue("name",check.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        }
        foreach(var i in m.UpOperations.OfType<CreateIndexOperation>()){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE c.relname=@name AND i.indisvalid AND i.indisunique=@unique",c);cmd.Parameters.AddWithValue("name",i.Name);cmd.Parameters.AddWithValue("unique",i.IsUnique);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid='public.bed_occupancies'::regclass AND conname='EX_bed_occupancies_time' AND contype='x' AND convalidated",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=12 AND bool_and(tgenabled='O') FROM pg_trigger WHERE NOT tgisinternal AND tgname IN ('medicare_transfers_immutable','medicare_ward_guard','medicare_bed_guard','medicare_admission_guard','medicare_occupancy_guard','medicare_transfer_guard','medicare_admission_bed_check','medicare_occupancy_history_check','medicare_transfer_history_check','medicare_outpatient_guard','medicare_admission_clinical_guard','medicare_outpatient_clinical_guard')",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        Console.WriteLine("Stay schema PASS: eight tables, columns, restrictive FKs, indexes, checks, bed exclusion and twelve enabled guards.");
    }
    private static async Task Integration(NpgsqlConnection c)
    {
        await using var tx=await c.BeginTransactionAsync();
        try
        {
            await using var db=Context(c);await db.Database.UseTransactionAsync(tx);var tag=Guid.NewGuid().ToString("N");var now=DateTime.UtcNow;
            var reception=new IdentityUser{Id="stay-reception-"+tag,UserName="stay-reception-"+tag,NormalizedUserName="STAY-RECEPTION-"+tag.ToUpperInvariant()};var account=new IdentityUser{Id="stay-doctor-"+tag,UserName="stay-doctor-"+tag,NormalizedUserName="STAY-DOCTOR-"+tag.ToUpperInvariant()};db.Users.AddRange(reception,account);
            db.UserRoles.AddRange(new IdentityUserRole<string>{UserId=reception.Id,RoleId=await db.Roles.Where(r=>r.Name=="Receptionist").Select(r=>r.Id).SingleAsync()},new IdentityUserRole<string>{UserId=account.Id,RoleId=await db.Roles.Where(r=>r.Name=="Doctor").Select(r=>r.Id).SingleAsync()});
            var patient=new Patient{PatientNumber="SYN-"+tag[..12],FirstName="Synthetic",LastName="Stay fixture",DateOfBirth=new DateOnly(2000,1,1),PhoneNumber="0000000000",CreatedAtUtc=now,UpdatedAtUtc=now};var department=new Department{Name="Synthetic "+tag,NormalizedName="SYNTHETIC "+tag.ToUpperInvariant()};var doctor=new Doctor{Department=department,RegistrationNumber="SYN-"+tag,FirstName="Synthetic",LastName="Stay fixture",IdentityUserId=account.Id};
            var ward=new Ward{Department=department,Code="SYN-"+tag.ToUpperInvariant(),Name="Synthetic ward"};var bed=new Bed{Ward=ward,Number="01"};var next=new Bed{Ward=ward,Number="02"};db.AddRange(patient,doctor,bed,next);await db.SaveChangesAsync();
            ControllerContext As(string user,string role)=>new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Role,role)},"RollbackFixture"))}};
            AdmissionsController Reception()=>new(db,TimeProvider.System){ControllerContext=As(reception.Id,"Receptionist")};AdmissionsController Doctor()=>new(db,TimeProvider.System){ControllerContext=As(account.Id,"Doctor")};
            var registration=new CareRegistrationForm{PatientId=patient.Id,DoctorId=doctor.Id,BedId=bed.Id,Token=Guid.NewGuid()};Require(await Reception().Create(registration) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();Require(await Reception().Create(registration) is RedirectToActionResult);
            var admission=await db.Admissions.SingleAsync(a=>a.CreationToken==registration.Token);Require(await db.BedOccupancies.CountAsync(o=>o.AdmissionId==admission.Id&&o.EndedAtUtc==null)==1);
            await Reject(c,tx,"UPDATE beds SET \"IsOperational\"=false WHERE \"Id\"=@id",bed.Id);
            await Reject(c,tx,"UPDATE admissions SET \"Status\"='DischargeReady', \"ClearedAtUtc\"=now(), \"ClearedByUserId\"=\"AdmittedByUserId\" WHERE \"Id\"=@id",admission.Id);
            // Closing a bed without matching transfer history must fail at the deferred boundary and roll back.
            await Reject(c,tx,"UPDATE bed_occupancies SET \"EndedAtUtc\"=clock_timestamp() WHERE \"AdmissionId\"=@id AND \"EndedAtUtc\" IS NULL",admission.Id);
            Require(await db.BedOccupancies.CountAsync(o=>o.AdmissionId==admission.Id&&o.EndedAtUtc==null)==1);
            await Reject(c,tx,"INSERT INTO bed_occupancies (\"Id\",\"AdmissionId\",\"BedId\",\"StartedAtUtc\") SELECT gen_random_uuid(),\"AdmissionId\",\"BedId\",clock_timestamp() FROM bed_occupancies WHERE \"AdmissionId\"=@id",admission.Id,"23505","23P01");
            // Two independently tracked provider contexts reject a stale facility version without committing fixtures.
            await using(var stale=Context(c))
            {
                await stale.Database.UseTransactionAsync(tx);var old=await stale.Beds.SingleAsync(b=>b.Id==next.Id);
                var fresh=await db.Beds.SingleAsync(b=>b.Id==next.Id);fresh.Version=Guid.NewGuid();await db.SaveChangesAsync();old.Version=Guid.NewGuid();
                var rejected=false;try{await stale.SaveChangesAsync();}catch(DbUpdateConcurrencyException){rejected=true;}Require(rejected);
            }
            Console.WriteLine("Rollback conflict checks PASS: occupied-bed protection, unauthorized clearance, deferred transfer rollback, duplicate occupancy rejection and PostgreSQL stale-version conflict.");
            Require(await Reception().Create(new CareRegistrationForm{PatientId=patient.Id,DoctorId=doctor.Id,BedId=next.Id,Token=Guid.NewGuid()}) is ViewResult);
            // Manual provider-level transfer uses the already-open outer transaction; controller HTTP transfer is covered separately.
            db.ChangeTracker.Clear();admission=await db.Admissions.SingleAsync(a=>a.Id==admission.Id);var current=await db.BedOccupancies.SingleAsync(o=>o.AdmissionId==admission.Id&&o.EndedAtUtc==null);var moved=DateTime.UtcNow;current.EndedAtUtc=moved;admission.Version=Guid.NewGuid();await db.SaveChangesAsync();
            db.BedOccupancies.Add(new BedOccupancy{AdmissionId=admission.Id,BedId=next.Id,StartedAtUtc=moved});db.BedTransfers.Add(new BedTransfer{AdmissionId=admission.Id,FromBedId=bed.Id,ToBedId=next.Id,Token=Guid.NewGuid(),OccurredAtUtc=moved,ActorUserId=reception.Id,Reason="Synthetic transfer"});await db.SaveChangesAsync();await Flush(c,tx);db.ChangeTracker.Clear();
            admission=await db.Admissions.SingleAsync(a=>a.Id==admission.Id);Require(await Doctor().Clinical(admission.Id,new ClinicalStayForm{ParentVersion=admission.Version,AdmissionReason="Synthetic",DischargeSummary="Synthetic discharge summary"}) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();
            admission=await db.Admissions.SingleAsync(a=>a.Id==admission.Id);Require(await Doctor().Clearance(admission.Id,admission.Version) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();Require(await db.BedOccupancies.AnyAsync(o=>o.AdmissionId==admission.Id&&o.EndedAtUtc==null));
            admission=await db.Admissions.SingleAsync(a=>a.Id==admission.Id);var dischargeToken=Guid.NewGuid();Require(await Reception().Discharge(admission.Id,admission.Version,dischargeToken) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();Require(!await db.BedOccupancies.AnyAsync(o=>o.AdmissionId==admission.Id&&o.EndedAtUtc==null));Require(await db.BedTransfers.CountAsync(t=>t.AdmissionId==admission.Id)==3);
            Require(await Reception().Discharge(admission.Id,admission.Version,dischargeToken) is RedirectToActionResult);
            OutpatientsController Visits(string user,string role)=>new(db,TimeProvider.System){ControllerContext=As(user,role)};
            var visitForm=new CareRegistrationForm{PatientId=patient.Id,DoctorId=doctor.Id,Token=Guid.NewGuid()};Require(await Visits(reception.Id,"Receptionist").Create(visitForm) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();var visit=await db.OutpatientVisits.SingleAsync(v=>v.CreationToken==visitForm.Token);
            Require(await Visits(account.Id,"Doctor").Progress(visit.Id,visit.Version,"Start") is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();visit=await db.OutpatientVisits.SingleAsync(v=>v.Id==visit.Id);
            Require(await Visits(account.Id,"Doctor").Clinical(visit.Id,new ClinicalStayForm{ParentVersion=visit.Version,FollowUpInformation="Synthetic follow-up"}) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();visit=await db.OutpatientVisits.SingleAsync(v=>v.Id==visit.Id);
            Require(await Visits(account.Id,"Doctor").Progress(visit.Id,visit.Version,"Complete") is RedirectToActionResult);await Flush(c,tx);
            await Reject(c,tx,"DELETE FROM bed_transfers WHERE \"AdmissionId\"=@id",admission.Id);
            await Reject(c,tx,"UPDATE admissions SET \"Status\"='Admitted' WHERE \"Id\"=@id",admission.Id);
            await Reject(c,tx,"UPDATE admission_clinical_notes SET \"DischargeSummary\"='Synthetic mutation' WHERE \"AdmissionId\"=@id",admission.Id);
            await Reject(c,tx,"UPDATE outpatient_visits SET \"Status\"='Registered' WHERE \"Id\"=@id",visit.Id);
            await Reject(c,tx,"UPDATE outpatient_clinical_notes SET \"FollowUpInformation\"='Synthetic mutation' WHERE \"OutpatientVisitId\"=@id",visit.Id);
            Console.WriteLine("Rollback stay workflow PASS: admission, idempotency, active-duplicate refusal, transfer/history, doctor clinical draft/clearance, checkout/bed release, outpatient consultation/follow-up/completion and terminal/history guards.");
        }
        finally{await tx.RollbackAsync();}
    }
    private static async Task Flush(NpgsqlConnection c,NpgsqlTransaction t){await using var cmd=new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED",c,t);await cmd.ExecuteNonQueryAsync();}
    private static async Task Reject(NpgsqlConnection c,NpgsqlTransaction t,string sql,Guid id,params string[] states){await t.SaveAsync("stay_guard");try{await using var cmd=new NpgsqlCommand(sql,c,t);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();await Flush(c,t);throw new InvalidOperationException("Expected guard did not reject.");}catch(PostgresException ex)when(states.Length==0?ex.SqlState=="23514":states.Contains(ex.SqlState)){await t.RollbackAsync("stay_guard");}await t.ReleaseAsync("stay_guard");}
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Hospital stay verification failed.");}
}
