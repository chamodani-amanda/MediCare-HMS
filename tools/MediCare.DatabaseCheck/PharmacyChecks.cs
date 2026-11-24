using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
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

internal static class PharmacyChecks
{
    private static string stage="database verification";
    internal const string Target="20261119072825_AddPharmacyManagement";
    internal const string Backup=@"D:\MediCare-Backups\medicare_dev_before_step7_20261119_072445_ef948f41.backup";
    internal const string Checksum="A817E14B7EE0AE0BBBC3029B326901C13F10F10CBD201B7AC4BCEE21A469F798";
    internal static async Task Run(string[] args)
    {
        try
        {
            var config=new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
            var cs=new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));Require(cs.Database=="medicare_dev");cs.Timeout=10;
            await using var connection=new NpgsqlConnection(cs.ConnectionString);await connection.OpenAsync();
            await using(var cmd=new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
            Console.WriteLine("Confirmed actual database: medicare_dev; schema: public.");
            var before=await Snapshot(connection);
            if(args.Contains("--pharmacy-preflight") || args.Contains("--apply-approved-pharmacy"))
            {
                await using var tx=await connection.BeginTransactionAsync();await using(var cmd=new NpgsqlCommand("SET TRANSACTION READ ONLY",connection,tx))await cmd.ExecuteNonQueryAsync();
                await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);await Preflight(db,connection,tx);await tx.CommitAsync();
            }
            if(args.Contains("--apply-approved-pharmacy"))
            {
                await using var application=Context(connection);
                Require((await application.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));
                await application.GetService<IMigrator>().MigrateAsync(Target);
                Console.WriteLine("Applied explicitly approved migration: "+Target);
            }
            if(!args.Contains("--pharmacy-preflight"))
            {
                await using var db=Context(connection);Require(!(await db.Database.GetPendingMigrationsAsync()).Any());Require((await db.Database.GetAppliedMigrationsAsync()).Contains(Target));
                await Schema(db,connection);
                if(args.Contains("--integration-pharmacy"))await Integration(connection);
            }
            await Preserved(connection,before);Console.WriteLine("Pharmacy checks passed; original records preserved.");
        }
        catch(Exception ex)
        {
            var cause=ex;while(cause.InnerException!=null)cause=cause.InnerException;
            Console.WriteLine("Pharmacy checks STOPPED during "+stage+": "+ex.GetType().Name+(cause is PostgresException p?"; SQLSTATE="+p.SqlState+"; constraint="+p.ConstraintName:"")+". Sensitive provider details suppressed.");Environment.ExitCode=1;
        }
    }
    private static void Require(bool condition){if(!condition)throw new InvalidOperationException("Pharmacy safety check failed.");}
    private static async Task Preflight(MediCareDbContext db,NpgsqlConnection connection,NpgsqlTransaction tx)
    {
        Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));
        var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[Target],db.Database.ProviderName!);
        Require(migration.UpOperations.All(o=>o is CreateTableOperation or CreateIndexOperation or AddColumnOperation or AddForeignKeyOperation or AddCheckConstraintOperation or SqlOperation));
        var names=new[]{"inventory_movements","medicine_batches","medicines","pharmacy_dispenses"};
        Require(migration.UpOperations.OfType<CreateTableOperation>().Select(t=>t.Name).OrderBy(n=>n).SequenceEqual(names));
        Require(migration.UpOperations.OfType<AddColumnOperation>().Count()==4 && migration.UpOperations.OfType<AddColumnOperation>().All(c=>c.Table=="medical_prescriptions" && c.IsNullable));
        Require(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys).All(f=>f.OnDelete==ReferentialAction.Restrict));
        var sql=migration.UpOperations.OfType<SqlOperation>().Single().Sql;Require(!sql.Contains("DROP ",StringComparison.OrdinalIgnoreCase)&&!sql.Contains("TRUNCATE",StringComparison.OrdinalIgnoreCase));
        Require(sql.Contains("FOR UPDATE")&&sql.Contains("DEFERRABLE INITIALLY DEFERRED"));
        static string Normalize(string value)=>value.Replace("\r\n","\n").Trim();
        Require(Normalize(db.GetService<IMigrator>().GenerateScript("20261119053856_AddLaboratoryManagement",Target))==Normalize(await File.ReadAllTextAsync("tools/PharmacyManagement-migration.sql")));
        await using(var cmd=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND relname=ANY(@names)) AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname LIKE 'medicare_pharmacy_%')",connection,tx)){cmd.Parameters.AddWithValue("names",names);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        await using(var stream=File.OpenRead(Backup))Require(stream.Length==85161 && Convert.ToHexString(await SHA256.HashDataAsync(stream))==Checksum);
        foreach(var option in new[]{"--list","--file=NUL"})
        {
            var info=new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(option);info.ArgumentList.Add(Backup);
            using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();await error;Require(process.ExitCode==0);var listing=await output;if(option=="--list")Require(listing.Contains("dbname: medicare_dev")&&listing.Contains("TABLE DATA"));
        }
        Console.WriteLine("Preflight PASS: sole pending pharmacy migration; four new tables, four nullable prescription columns; restrictive FKs; no destructive Up operations or name collisions; SQL artifact matches migration.");
        Console.WriteLine("Backup PASS: 85,161 bytes; SHA-256 unchanged; archive listing and full offline read passed. No restore performed.");
    }
    private static async Task Schema(MediCareDbContext db,NpgsqlConnection connection)
    {
        var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[Target],db.Database.ProviderName!);
        foreach(var table in migration.UpOperations.OfType<CreateTableOperation>())
        {
            await using(var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND contype='p' AND convalidated",connection)){cmd.Parameters.AddWithValue("table",table.Name);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var column in table.Columns){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column AND is_nullable=@nullable",connection);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("column",column.Name);cmd.Parameters.AddWithValue("nullable",column.IsNullable?"YES":"NO");Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var fk in table.ForeignKeys){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND confdeltype='r' AND convalidated",connection);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",fk.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var check in table.CheckConstraints){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND contype='c' AND convalidated",connection);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",check.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        }
        foreach(var index in migration.UpOperations.OfType<CreateIndexOperation>()){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE c.relname=@name AND i.indisvalid AND i.indisunique=@unique",connection);cmd.Parameters.AddWithValue("name",index.Name);cmd.Parameters.AddWithValue("unique",index.IsUnique);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        foreach(var column in migration.UpOperations.OfType<AddColumnOperation>()){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name='medical_prescriptions' AND column_name=@name AND is_nullable='YES'",connection);cmd.Parameters.AddWithValue("name",column.Name);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid='public.medical_prescriptions'::regclass AND conname='CK_medical_prescriptions_authorization' AND convalidated",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid='public.medical_prescriptions'::regclass AND conname='FK_medical_prescriptions_medicines_MedicineId' AND confrelid='public.medicines'::regclass AND confdeltype='r' AND convalidated",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=2 AND bool_and(tgdeferrable AND tginitdeferred) FROM pg_trigger WHERE tgname IN ('medicare_dispense_balance','medicare_movement_balance')",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=7 AND bool_and(tgenabled='O') FROM pg_trigger WHERE NOT tgisinternal AND tgname IN ('medicare_emr_record_guard','medicare_emr_revision_guard','medicare_emr_prescription_guard','medicare_lab_request_guard','medicare_lab_result_guard','medicare_lab_request_completion','medicare_lab_result_completion')",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=10 AND bool_and(tgenabled='O') FROM pg_trigger WHERE NOT tgisinternal AND tgname IN ('medicare_movement_immutable','medicare_dispense_immutable','medicare_medicine_guard','medicare_batch_guard','medicare_pharmacy_prescription_guard','medicare_pharmacy_finalize_guard','medicare_dispense_guard','medicare_movement_guard','medicare_dispense_balance','medicare_movement_balance')",connection))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        Console.WriteLine("Schema PASS: pharmacy tables, columns, restrictive FKs, checks, indexes and ten enabled guards.");
        Console.WriteLine("Schema PASS: four nullable prescription authorization fields, medicine FK, authorization check, deferred allocation checks and all seven original EMR/laboratory history guards.");
    }
    private static async Task Integration(NpgsqlConnection connection)
    {
        stage="rollback fixture creation";
        await using var tx=await connection.BeginTransactionAsync();
        try
        {
            await using var db=Context(connection);await db.Database.UseTransactionAsync(tx);var tag=Guid.NewGuid().ToString("N");var now=DateTime.UtcNow;var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(now,"Asia/Colombo"));
            var pharmacist=new IdentityUser{Id="synthetic-pharm-"+tag,UserName="synthetic-pharm-"+tag,NormalizedUserName="SYNTHETIC-PHARM-"+tag.ToUpperInvariant()};var doctorUser=new IdentityUser{Id="synthetic-doctor-"+tag,UserName="synthetic-doctor-"+tag,NormalizedUserName="SYNTHETIC-DOCTOR-"+tag.ToUpperInvariant()};
            db.Users.AddRange(pharmacist,doctorUser);db.UserRoles.AddRange(new IdentityUserRole<string>{UserId=pharmacist.Id,RoleId=await db.Roles.Where(r=>r.Name=="Pharmacist").Select(r=>r.Id).SingleAsync()},new IdentityUserRole<string>{UserId=doctorUser.Id,RoleId=await db.Roles.Where(r=>r.Name=="Doctor").Select(r=>r.Id).SingleAsync()});
            var patient=new Patient{PatientNumber="SYN-"+tag[..12],FirstName="Synthetic",LastName="Pharmacy fixture",DateOfBirth=new DateOnly(2000,1,1),Gender="Other",PhoneNumber="0000000000",CreatedAtUtc=now,UpdatedAtUtc=now};
            var department=new Department{Name="Synthetic "+tag,NormalizedName="SYNTHETIC "+tag.ToUpperInvariant()};
            var doctor=new Doctor{Department=department,RegistrationNumber="SYN-"+tag,FirstName="Synthetic",LastName="Pharmacy fixture",Qualifications="Synthetic",Specialization="Synthetic",Email="synthetic-"+tag+"@invalid.example",NormalizedEmail="SYNTHETIC-"+tag.ToUpperInvariant()+"@INVALID.EXAMPLE",PhoneNumber="0000000000",IdentityUserId=doctorUser.Id};
            var start=new DateTime(now.Year,now.Month,now.Day,0,0,0,DateTimeKind.Utc).AddDays(2);
            var appointment=new Appointment{Patient=patient,Doctor=doctor,BookingToken=Guid.NewGuid(),StartAtUtc=start,EndAtUtc=start.AddMinutes(30),DurationMinutes=30,CreatedAtUtc=now,UpdatedAtUtc=now};
            var record=new MedicalRecord{Patient=patient,Doctor=doctor,Appointment=appointment,AuthorUserId=doctorUser.Id,CreationToken=Guid.NewGuid(),CreatedAtUtc=now};
            var revision=new MedicalRecordRevision{MedicalRecord=record,Number=1,AuthorUserId=doctorUser.Id,CreatedAtUtc=now,UpdatedAtUtc=now,Symptoms="Synthetic",Diagnosis="Synthetic",Treatment="Synthetic"};
            var medicine=new Medicine{Code="SYN-"+tag.ToUpperInvariant(),Name="Synthetic medicine",GenericName="Synthetic",Strength="Synthetic",Formulation="Synthetic",DispensingUnit="unit"};
            var rx=new MedicalPrescription{MedicalRecordRevision=revision,Medicine=medicine,LineageId=Guid.NewGuid(),AuthorizedQuantity=10,DispensingUnit="unit",Medication=medicine.Name,Dosage="Synthetic",Route="Synthetic",Frequency="Synthetic",Duration="Synthetic"};
            db.Add(rx);await db.SaveChangesAsync();revision.FinalizedAtUtc=DateTime.UtcNow;revision.UpdatedAtUtc=revision.FinalizedAtUtc.Value;revision.Version=Guid.NewGuid();await db.SaveChangesAsync();
            PharmacyController Controller(MediCareDbContext context)=>new(context,TimeProvider.System){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,pharmacist.Id),new Claim(ClaimTypes.Role,"Pharmacist")},"RollbackFixture"))}}};
            var receive=new StockForm{MedicineId=medicine.Id,Token=Guid.NewGuid(),BatchNumber="SYN-BATCH",Supplier="Synthetic",ExpiresOn=today.AddYears(1),Quantity=20,Reason="Synthetic receipt"};Require(await Controller(db).Receive(receive) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();
            var batch=await db.MedicineBatches.SingleAsync(b=>b.MedicineId==medicine.Id);Require(batch.AvailableQuantity==20);
            var form=new DispenseForm{PrescriptionId=rx.Id,Token=Guid.NewGuid(),Quantity=4};Require(await Controller(db).Dispense(form) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();Require((await db.MedicineBatches.SingleAsync(b=>b.Id==batch.Id)).AvailableQuantity==16);
            Require(await Controller(db).Dispense(form) is RedirectToActionResult);Require(await db.PharmacyDispenses.CountAsync(d=>d.LineageId==rx.LineageId)==1);
            Require(await Controller(db).Dispense(new DispenseForm{PrescriptionId=rx.Id,Token=Guid.NewGuid(),Quantity=7}) is ViewResult);
            db.ChangeTracker.Clear();Require(await Controller(db).Dispense(new DispenseForm{PrescriptionId=rx.Id,Token=Guid.NewGuid(),Quantity=6}) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();
            Require((await db.MedicineBatches.SingleAsync(b=>b.Id==batch.Id)).AvailableQuantity==10);Require(await db.PharmacyDispenses.Where(d=>d.LineageId==rx.LineageId).SumAsync(d=>d.Quantity)==10);
            var issued=await db.PharmacyDispenses.Where(d=>d.Token==form.Token).SingleAsync();Require(await Controller(db).Reverse(issued.Id,Guid.NewGuid(),"Synthetic returned goods",true) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();Require((await db.MedicineBatches.SingleAsync(b=>b.Id==batch.Id)).AvailableQuantity==10);Require(await db.PharmacyDispenses.Where(d=>d.LineageId==rx.LineageId).SumAsync(d=>d.Quantity)==6);
            await Rejected(connection,tx,"UPDATE medicine_batches SET \"AvailableQuantity\"=100 WHERE \"Id\"=@id",batch.Id);
            await Rejected(connection,tx,"DELETE FROM inventory_movements WHERE \"BatchId\"=@id",batch.Id);
            await Rejected(connection,tx,"UPDATE pharmacy_dispenses SET \"Quantity\"=1 WHERE \"Id\"=@id",issued.Id);
            await Rejected(connection,tx,"UPDATE medical_prescriptions SET \"AuthorizedQuantity\"=100 WHERE \"Id\"=@id",rx.Id);
            await Rejected(connection,tx,"INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",'Adjust',-100,'Synthetic unauthorized adjustment','not-a-pharmacist',clock_timestamp() FROM medicine_batches WHERE \"Id\"=@id",batch.Id);
            stage="extended rollback pharmacy guards";
            // Each expected database rejection gets its own savepoint; no invalid effect survives.
            await Rejected(connection,tx,"INSERT INTO medicines (\"Id\",\"Code\",\"Name\",\"GenericName\",\"Strength\",\"Formulation\",\"DispensingUnit\",\"LowStockThreshold\",\"IsActive\",\"Version\") SELECT gen_random_uuid(),\"Code\",\"Name\",\"GenericName\",\"Strength\",\"Formulation\",\"DispensingUnit\",0,true,gen_random_uuid() FROM medicines WHERE \"Id\"=@id",medicine.Id,"23505");
            await Rejected(connection,tx,"INSERT INTO medicine_batches SELECT gen_random_uuid(),\"MedicineId\",\"BatchNumber\",\"Supplier\",\"ManufacturedOn\",\"ExpiresOn\",\"ReceivedAtUtc\",0,gen_random_uuid() FROM medicine_batches WHERE \"Id\"=@id",batch.Id,"23505");
            var stockBefore=await db.MedicineBatches.AsNoTracking().Where(b=>b.Id==batch.Id).Select(b=>b.AvailableQuantity).SingleAsync();
            await Rejected(connection,tx,"INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\") SELECT gen_random_uuid(),\"Token\",\"BatchId\",'Receive',1,'Synthetic duplicate',\"ActorUserId\",clock_timestamp() FROM inventory_movements WHERE \"BatchId\"=@id AND \"Kind\"='Receive' LIMIT 1",batch.Id,"23505");
            Require(await db.MedicineBatches.AsNoTracking().Where(b=>b.Id==batch.Id).Select(b=>b.AvailableQuantity).SingleAsync()==stockBefore);
            await Rejected(connection,tx,"INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\",\"ExpectedBatchVersion\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",'Adjust',-100,'Synthetic oversell',@actor,clock_timestamp(),\"Version\" FROM medicine_batches WHERE \"Id\"=@id",batch.Id,actor:pharmacist.Id);
            await Rejected(connection,tx,"INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\",\"ExpectedBatchVersion\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",'Adjust',1,'Synthetic stale version',@actor,clock_timestamp(),gen_random_uuid() FROM medicine_batches WHERE \"Id\"=@id",batch.Id,actor:pharmacist.Id);
            db.ChangeTracker.Clear();var currentBatch=await db.MedicineBatches.AsNoTracking().SingleAsync(b=>b.Id==batch.Id);
            var adjustment=new StockForm{BatchId=batch.Id,Version=currentBatch.Version,Token=Guid.NewGuid(),Quantity=1,Reason="Synthetic adjustment"};Require(await Controller(db).Adjust(adjustment) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();
            var stale=Controller(db);Require(await stale.Adjust(new StockForm{BatchId=batch.Id,Version=currentBatch.Version,Token=Guid.NewGuid(),Quantity=1,Reason="Synthetic stale adjustment"}) is ViewResult);Require(!stale.ModelState.IsValid);db.ChangeTracker.Clear();
            Require((await db.MedicineBatches.AsNoTracking().SingleAsync(b=>b.Id==batch.Id)).AvailableQuantity==stockBefore+1);

            foreach(var receipt in new[]{("SYN-SOON",30,3m),("SYN-LATER",60,5m)})
            {Require(await Controller(db).Receive(new StockForm{MedicineId=medicine.Id,Token=Guid.NewGuid(),BatchNumber=receipt.Item1,Supplier="Synthetic",ExpiresOn=today.AddDays(receipt.Item2),Quantity=receipt.Item3,Reason="Synthetic FEFO receipt"}) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();}
            var fefo=new DispenseForm{PrescriptionId=rx.Id,Token=Guid.NewGuid(),Quantity=4};Require(await Controller(db).Dispense(fefo) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();
            var fefoMoves=await db.InventoryMovements.Where(m=>m.Dispense!.Token==fefo.Token).Include(m=>m.Batch).ToListAsync();Require(fefoMoves.Count==2 && fefoMoves.Single(m=>m.Batch.BatchNumber=="SYN-SOON").Quantity==-3 && fefoMoves.Single(m=>m.Batch.BatchNumber=="SYN-LATER").Quantity==-1);
            var expired=new MedicineBatch{MedicineId=medicine.Id,BatchNumber="SYN-EXPIRED",Supplier="Synthetic",ExpiresOn=today,ReceivedAtUtc=DateTime.UtcNow};db.Add(expired);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            await Rejected(connection,tx,"INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\") VALUES(gen_random_uuid(),gen_random_uuid(),@id,'Receive',1,'Synthetic expired receipt',@actor,clock_timestamp())",expired.Id,actor:pharmacist.Id,expectedMessage:"Expired stock unavailable");
            Console.WriteLine("PostgreSQL PASS: duplicate batch/token protection, atomic rollback after failed ledger writes, insufficient stock, current/stale adjustment versions, multi-batch FEFO and expired receipt rejection.");

            stage="rollback amendment lineage guards";
            var amendment=new MedicalRecordRevision{MedicalRecordId=record.Id,AuthorUserId=doctorUser.Id,Number=2,AmendmentReason="Synthetic pharmacy amendment",CreatedAtUtc=DateTime.UtcNow,UpdatedAtUtc=DateTime.UtcNow,Symptoms="Synthetic",Diagnosis="Synthetic",Treatment="Synthetic"};
            var amendedRx=new MedicalPrescription{MedicalRecordRevision=amendment,MedicineId=medicine.Id,LineageId=rx.LineageId,AuthorizedQuantity=20,DispensingUnit="unit",Medication=medicine.Name,Dosage="Synthetic",Route="Synthetic",Frequency="Synthetic",Duration="Synthetic"};db.Add(amendedRx);await db.SaveChangesAsync();db.ChangeTracker.Clear();
            Require(!await MediCare.Web.Services.PharmacyRules.Eligible(db).AnyAsync(p=>p.Id==rx.Id));
            await Rejected(connection,tx,"UPDATE medical_prescriptions SET \"AuthorizedQuantity\"=1 WHERE \"Id\"=@id",amendedRx.Id);
            await Rejected(connection,tx,"INSERT INTO medical_prescriptions (\"Id\",\"MedicalRecordRevisionId\",\"Position\",\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\",\"Instructions\",\"MedicineId\",\"LineageId\",\"AuthorizedQuantity\",\"DispensingUnit\") SELECT gen_random_uuid(),\"MedicalRecordRevisionId\",1,\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\",\"Instructions\",\"MedicineId\",\"LineageId\",\"AuthorizedQuantity\",\"DispensingUnit\" FROM medical_prescriptions WHERE \"Id\"=@id",amendedRx.Id,"23505");
            var alternate=new Medicine{Code="ALT-"+tag.ToUpperInvariant(),Name="Synthetic alternate",GenericName="Synthetic",Strength="Different",Formulation="Synthetic",DispensingUnit="unit"};db.Add(alternate);await db.SaveChangesAsync();
            await Rejected(connection,tx,"UPDATE medical_prescriptions SET \"MedicineId\"=(SELECT \"Id\" FROM medicines WHERE \"Code\" LIKE 'ALT-%' LIMIT 1) WHERE \"Id\"=@id",amendedRx.Id);
            var editable=await db.MedicalRecordRevisions.SingleAsync(r=>r.Id==amendment.Id);editable.FinalizedAtUtc=DateTime.UtcNow;editable.UpdatedAtUtc=editable.FinalizedAtUtc.Value;editable.Version=Guid.NewGuid();await db.SaveChangesAsync();await Flush(connection,tx);db.ChangeTracker.Clear();
            Require(await db.PharmacyDispenses.Where(d=>d.LineageId==rx.LineageId).SumAsync(d=>d.Quantity)==10);Require(await MediCare.Web.Services.PharmacyRules.Eligible(db).AnyAsync(p=>p.Id==amendedRx.Id));
            var exception=new DispenseForm{PrescriptionId=amendedRx.Id,Token=Guid.NewGuid(),Quantity=2,ExceptionBatchId=batch.Id,ExceptionReason="Synthetic documented FEFO exception"};Require(await Controller(db).Dispense(exception) is RedirectToActionResult);await Flush(connection,tx);db.ChangeTracker.Clear();
            const string issueWithoutAllocation="INSERT INTO pharmacy_dispenses (\"Id\",\"Token\",\"PrescriptionId\",\"LineageId\",\"Quantity\",\"ActorUserId\",\"OccurredAtUtc\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",\"LineageId\",1,@actor,clock_timestamp() FROM medical_prescriptions WHERE \"Id\"=@id";
            await Rejected(connection,tx,issueWithoutAllocation,amendedRx.Id,actor:pharmacist.Id,expectedMessage:"Dispensing and batch allocations must balance atomically");
            const string issueAllocation="WITH issued AS (INSERT INTO pharmacy_dispenses (\"Id\",\"Token\",\"PrescriptionId\",\"LineageId\",\"Quantity\",\"ActorUserId\",\"OccurredAtUtc\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",\"LineageId\",1,@actor,clock_timestamp() FROM medical_prescriptions WHERE \"Id\"=@rx RETURNING \"Id\") INSERT INTO inventory_movements (\"Id\",\"Token\",\"BatchId\",\"DispenseId\",\"Kind\",\"Quantity\",\"Reason\",\"ActorUserId\",\"OccurredAtUtc\") SELECT gen_random_uuid(),gen_random_uuid(),@id,\"Id\",'Dispense',-1,'Synthetic guard test',@actor,clock_timestamp() FROM issued";
            await Rejected(connection,tx,issueAllocation,batch.Id,actor:pharmacist.Id,rx:amendedRx.Id,expectedMessage:"FEFO exception requires a documented reason"); // Earlier stock still exists; no exception reason.
            await Rejected(connection,tx,issueAllocation,expired.Id,actor:pharmacist.Id,rx:amendedRx.Id,expectedMessage:"Expired stock unavailable");
            Require(await db.PharmacyDispenses.Where(d=>d.LineageId==rx.LineageId).SumAsync(d=>d.Quantity)==12);
            var otherAppointment=new Appointment{PatientId=patient.Id,DoctorId=doctor.Id,BookingToken=Guid.NewGuid(),StartAtUtc=start.AddHours(2),EndAtUtc=start.AddHours(2).AddMinutes(30),DurationMinutes=30,CreatedAtUtc=DateTime.UtcNow,UpdatedAtUtc=DateTime.UtcNow};
            var otherRevision=new MedicalRecordRevision{MedicalRecord=new MedicalRecord{PatientId=patient.Id,DoctorId=doctor.Id,Appointment=otherAppointment,AuthorUserId=doctorUser.Id,CreationToken=Guid.NewGuid(),CreatedAtUtc=DateTime.UtcNow},Number=1,AuthorUserId=doctorUser.Id,CreatedAtUtc=DateTime.UtcNow,UpdatedAtUtc=DateTime.UtcNow};db.Add(otherRevision);await db.SaveChangesAsync();
            await Rejected(connection,tx,"INSERT INTO medical_prescriptions (\"Id\",\"MedicalRecordRevisionId\",\"Position\",\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\",\"MedicineId\",\"LineageId\",\"AuthorizedQuantity\",\"DispensingUnit\") SELECT gen_random_uuid(),@rx,0,\"Medication\",\"Dosage\",\"Route\",\"Frequency\",\"Duration\",\"MedicineId\",\"LineageId\",\"AuthorizedQuantity\",\"DispensingUnit\" FROM medical_prescriptions WHERE \"Id\"=@id",amendedRx.Id,rx:otherRevision.Id,expectedMessage:"Prescription lineage cannot change patient or medicine");
            Console.WriteLine("PostgreSQL PASS: draft suspension, amendment authorization fields, preserved net lineage balance, duplicate/changed-medicine lineage rejection, superseded-order exclusion, documented FEFO exception, expired dispensing rejection and deferred allocation rollback.");
            Console.WriteLine("PostgreSQL rollback workflow PASS: receipt, live balances, partial/full dispensing, remaining cap, idempotency, quarantine reversal, append-only and finalized-history protection, unauthorized ledger rejection.");
        }
        finally{await tx.RollbackAsync();}
    }
    private static async Task Flush(NpgsqlConnection c,NpgsqlTransaction t){await using var cmd=new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED",c,t);await cmd.ExecuteNonQueryAsync();}
    private static async Task Rejected(NpgsqlConnection c,NpgsqlTransaction t,string sql,Guid id,string state="23514",string? actor=null,Guid? rx=null,string? expectedMessage=null)
    {
        await t.SaveAsync("pharmacy_guard");try{await using var cmd=new NpgsqlCommand(sql,c,t);cmd.Parameters.AddWithValue("id",id);if(actor!=null)cmd.Parameters.AddWithValue("actor",actor);if(rx.HasValue)cmd.Parameters.AddWithValue("rx",rx.Value);await cmd.ExecuteNonQueryAsync();await Flush(c,t);throw new InvalidOperationException("Expected guard did not reject.");}catch(PostgresException ex)when(ex.SqlState==state){await t.RollbackAsync("pharmacy_guard");Require(expectedMessage==null||ex.MessageText==expectedMessage);}await t.ReleaseAsync("pharmacy_guard");
    }
}
