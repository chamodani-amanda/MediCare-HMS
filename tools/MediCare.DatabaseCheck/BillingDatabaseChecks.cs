using System.Diagnostics;
using System.Security.Cryptography;
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

internal static class BillingDatabaseChecks
{
    internal const string Target="20261119125827_AddBillingAndPayments";
    internal const string Backup=@"D:\MediCare-Backups\medicare_dev_before_step7_20261119_130852_fcfadaa4.backup";
    internal const string Checksum="86522D11A48FD96202B00DB2ED943B881F6E50355F3444AAE2BF997787E7CBFF";
    private static string stage="target confirmation";
    private static readonly string[] Tables={"billing_invoice_lines","billing_invoices","billing_payments","billing_prices","billing_services","patient_charges"};
    internal static async Task Run(string[] args)
    {
        try
        {
            var config=new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
            var cs=new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));Require(cs.Database=="medicare_dev");cs.Timeout=10;
            await using var c=new NpgsqlConnection(cs.ConnectionString);await c.OpenAsync();await using(var cmd=new NpgsqlCommand("SELECT current_database()='medicare_dev' AND current_schema()='public'",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);Console.WriteLine("Confirmed actual database: medicare_dev.");
            var before=await Snapshot(c);await using var db=Context(c);
            if(args.Contains("--billing-preflight")||args.Contains("--apply-approved-billing"))
            {
                await using var tx=await c.BeginTransactionAsync();await using(var ro=new NpgsqlCommand("SET TRANSACTION READ ONLY",c,tx))await ro.ExecuteNonQueryAsync();await db.Database.UseTransactionAsync(tx);await Preflight(db,c,tx);await tx.RollbackAsync();
            }
            if(args.Contains("--apply-approved-billing"))
            {
                stage="approved migration application";await using var application=Context(c);
                Require((await application.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));
                await application.GetService<IMigrator>().MigrateAsync(Target);
                Console.WriteLine("Applied approved migration to medicare_dev: "+Target);
            }
            if(!args.Contains("--billing-preflight"))
            {
                await using var verification=Context(c);Require(!(await verification.Database.GetPendingMigrationsAsync()).Any());Require((await verification.Database.GetAppliedMigrationsAsync()).Contains(Target));await Schema(verification,c);if(args.Contains("--integration-billing"))await Integration(c);
            }
            await Preserved(c,before);Console.WriteLine("Billing checks passed; original records preserved.");
        }
        catch(Exception ex){var cause=ex;while(cause.InnerException!=null)cause=cause.InnerException;Console.WriteLine("Billing checks STOPPED during "+stage+": "+ex.GetType().Name+(cause is PostgresException p?"; SQLSTATE="+p.SqlState+"; constraint="+p.ConstraintName:"")+". Sensitive details suppressed.");Environment.ExitCode=1;}
    }
    private static Migration Migration(MediCareDbContext db){var a=db.GetService<IMigrationsAssembly>();return a.CreateMigration(a.Migrations[Target],db.Database.ProviderName!);}
    private static async Task Preflight(MediCareDbContext db,NpgsqlConnection c,NpgsqlTransaction tx)
    {
        stage="additive migration review";Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));var m=Migration(db);
        Require(m.UpOperations.All(o=>o is CreateTableOperation or CreateIndexOperation or CreateSequenceOperation or SqlOperation));Require(m.UpOperations.OfType<CreateTableOperation>().Select(t=>t.Name).Order().SequenceEqual(Tables));Require(m.UpOperations.OfType<CreateSequenceOperation>().Select(x=>x.Name).Order().SequenceEqual(new[]{"billing_invoice_numbers","billing_receipt_numbers"}));Require(m.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys).All(f=>f.OnDelete==ReferentialAction.Restrict));
        var sql=m.UpOperations.OfType<SqlOperation>().Single().Sql;Require(!sql.Contains("DROP ",StringComparison.OrdinalIgnoreCase)&&!sql.Contains("TRUNCATE",StringComparison.OrdinalIgnoreCase)&&!sql.Contains("DELETE FROM",StringComparison.OrdinalIgnoreCase));Require(sql.Contains("FOR UPDATE")&&sql.Contains("DEFERRABLE INITIALLY DEFERRED"));
        static string Normalize(string s)=>s.Replace("\r\n","\n").Trim();Require(Normalize(db.GetService<IMigrator>().GenerateScript("20261119090005_AddInpatientAndOutpatientManagement",Target))==Normalize(await File.ReadAllTextAsync("tools/Billing-migration.sql")));
        await using(var cmd=new NpgsqlCommand("SELECT NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND (relname=ANY(@names) OR relname IN ('billing_invoice_numbers','billing_receipt_numbers'))) AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname LIKE 'medicare_billing_%') AND to_regprocedure('public.medicare_stay_role(text,text)') IS NOT NULL",c,tx)){cmd.Parameters.AddWithValue("names",Tables);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        stage="backup verification";await using(var stream=File.OpenRead(Backup))Require(stream.Length==177915&&Convert.ToHexString(await SHA256.HashDataAsync(stream))==Checksum);
        foreach(var option in new[]{"--list","--file=NUL"}){var info=new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(option);info.ArgumentList.Add(Backup);using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await error;Require(p.ExitCode==0);var listing=await output;if(option=="--list")Require(listing.Contains("dbname: medicare_dev")&&listing.Contains("TABLE DATA"));}
        Console.WriteLine("Billing preflight PASS: sole pending migration; six new tables, two sequences, restrictive FKs and new financial guards only; SQL artifact matches; no object collisions. Existing tables are not altered.");Console.WriteLine("Backup PASS: 177,915 bytes; SHA-256 matches; archive listing and complete offline read passed. No migration applied.");
    }
    private static async Task Schema(MediCareDbContext db,NpgsqlConnection c)
    {
        stage="billing schema";var m=Migration(db);foreach(var table in m.UpOperations.OfType<CreateTableOperation>()){
            foreach(var column in table.Columns){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM information_schema.columns WHERE table_schema='public' AND table_name=@table AND column_name=@column AND is_nullable=@nullable",c);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("column",column.Name);cmd.Parameters.AddWithValue("nullable",column.IsNullable?"YES":"NO");Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var fk in table.ForeignKeys){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND confdeltype='r' AND convalidated",c);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",fk.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
            foreach(var check in table.CheckConstraints){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_constraint WHERE conrelid=@table::regclass AND conname=@name AND contype='c' AND convalidated",c);cmd.Parameters.AddWithValue("table",table.Name);cmd.Parameters.AddWithValue("name",check.Name!);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        }
        foreach(var index in m.UpOperations.OfType<CreateIndexOperation>()){await using var cmd=new NpgsqlCommand("SELECT count(*)=1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid WHERE c.relname=@name AND i.indisvalid AND i.indisunique=@unique",c);cmd.Parameters.AddWithValue("name",index.Name);cmd.Parameters.AddWithValue("unique",index.IsUnique);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=12 AND bool_and(tgenabled='O') FROM pg_trigger WHERE NOT tgisinternal AND tgname LIKE 'billing_%'",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=3 AND bool_and(tgdeferrable AND tginitdeferred) FROM pg_trigger WHERE tgname IN ('billing_invoice_balance','billing_line_balance','billing_payment_balance')",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=2 AND bool_and(data_type='bigint'::regtype AND increment_by=1 AND NOT cycle) FROM pg_sequences WHERE schemaname='public' AND sequencename IN ('billing_invoice_numbers','billing_receipt_numbers')",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        await using(var cmd=new NpgsqlCommand("SELECT count(*)=2 FROM information_schema.columns WHERE table_schema='public' AND column_name='Number' AND ((table_name='billing_invoices' AND column_default LIKE '%billing_invoice_numbers%') OR (table_name='billing_payments' AND column_default LIKE '%billing_receipt_numbers%'))",c))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        Console.WriteLine("Billing schema PASS: six tables/columns, financial FKs, checks, indexes, two bigint sequences/defaults, twelve enabled guards and three initially deferred ledger checks.");
    }
    private static async Task Integration(NpgsqlConnection c)
    {
        stage="rollback-isolated financial fixtures";await using var tx=await c.BeginTransactionAsync();
        try
        {
            await using var db=Context(c);await db.Database.UseTransactionAsync(tx);var tag=Guid.NewGuid().ToString("N");var admin=new IdentityUser{Id="bill-admin-"+tag,UserName="bill-admin-"+tag};var accountant=new IdentityUser{Id="bill-accountant-"+tag,UserName="bill-accountant-"+tag};db.Users.AddRange(admin,accountant);foreach(var pair in new[]{(admin,"Administrator"),(accountant,"Accountant")})db.UserRoles.Add(new IdentityUserRole<string>{UserId=pair.Item1.Id,RoleId=await db.Roles.Where(r=>r.Name==pair.Item2).Select(r=>r.Id).SingleAsync()});
            var patient=new Patient{PatientNumber="SYN-"+tag[..12],FirstName="Synthetic",LastName="Billing fixture"};var service=new BillingService{Code="SYN-"+tag.ToUpperInvariant(),Name="Synthetic configured price",Category="Other"};var now=DateTime.UtcNow;var price=new BillingPrice{Service=service,UnitPrice=100,CreatedAtUtc=now,EffectiveAtUtc=now,ActorUserId=admin.Id};db.AddRange(patient,price);await db.SaveChangesAsync();
            BillingController Controller()=>new(db,TimeProvider.System){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,accountant.Id),new Claim(ClaimTypes.Role,"Accountant")},"RollbackFixture"))}}};
            var chargeForm=new ChargeForm{Token=Guid.NewGuid(),PatientId=patient.Id,ServiceId=service.Id,Quantity=1};Require(await Controller().Charge(chargeForm) is RedirectToActionResult);await Flush(c,tx);Require(await Controller().Charge(chargeForm) is RedirectToActionResult);var charge=await db.PatientCharges.SingleAsync(x=>x.Token==chargeForm.Token);Require(charge.Quantity==1m&&charge.UnitPrice==100m&&charge.Total==100m);
            await Reject(c,tx,"UPDATE billing_prices SET \"UnitPrice\"=1 WHERE \"Id\"=@id",price.Id);await Reject(c,tx,"UPDATE patient_charges SET \"Total\"=1 WHERE \"Id\"=@id",charge.Id);
            stage="draft invoice and issuance";Require(await Controller().Draft(new InvoiceForm{Token=Guid.NewGuid(),PatientId=patient.Id,ChargeIds=new(){charge.Id}}) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();var invoice=await db.BillingInvoices.SingleAsync(x=>x.PatientId==patient.Id);Require(await Controller().Issue(invoice.Id,invoice.Version) is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();invoice=await db.BillingInvoices.SingleAsync(x=>x.Id==invoice.Id);
            stage="partial payment and duplicate submission";var payment=new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=Guid.NewGuid(),Amount=40};Require(await Controller().Pay(payment) is RedirectToActionResult);await Flush(c,tx);Require(await Controller().Pay(payment) is RedirectToActionResult);db.ChangeTracker.Clear();invoice=await db.BillingInvoices.SingleAsync(x=>x.Id==invoice.Id);
            Require(invoice.Total==100m);Require((await db.BillingPayments.Where(p=>p.InvoiceId==invoice.Id).SumAsync(p=>p.ReversesId==null?p.Amount:-p.Amount))==40m);
            Console.WriteLine("LKR partial balance PASS: total 100.00; net paid 40.00; outstanding 60.00 (synthetic fixture).");
            stage="overpayment and invalid actor rejection";
            await Reject(c,tx,"INSERT INTO billing_payments (\"Id\",\"Token\",\"InvoiceId\",\"Amount\",\"Method\",\"RecordedAtUtc\",\"ActorUserId\") SELECT gen_random_uuid(),gen_random_uuid(),\"Id\",61,'Cash',clock_timestamp(),\"ActorUserId\" FROM billing_invoices WHERE \"Id\"=@id",invoice.Id);
            await Reject(c,tx,"INSERT INTO billing_payments (\"Id\",\"Token\",\"InvoiceId\",\"Amount\",\"Method\",\"RecordedAtUtc\",\"ActorUserId\") SELECT gen_random_uuid(),gen_random_uuid(),i.\"Id\",1,'Cash',clock_timestamp(),p.\"ActorUserId\" FROM billing_invoices i CROSS JOIN billing_prices p WHERE i.\"Id\"=@id AND p.\"Id\"='"+price.Id+"'::uuid",invoice.Id);
            stage="deferred financial rollback";
            var count=await db.BillingInvoices.CountAsync();await Reject(c,tx,"INSERT INTO billing_invoices (\"Id\",\"Token\",\"PatientId\",\"State\",\"Total\",\"CreatedAtUtc\",\"ActorUserId\",\"Version\") SELECT gen_random_uuid(),gen_random_uuid(),\"PatientId\",'Draft',100,clock_timestamp(),\"ActorUserId\",gen_random_uuid() FROM billing_invoices WHERE \"Id\"=@id",invoice.Id);Require(await db.BillingInvoices.CountAsync()==count);
            stage="PostgreSQL stale invoice version";
            await using(var stale=Context(c)){await stale.Database.UseTransactionAsync(tx);var old=await stale.BillingInvoices.SingleAsync(i=>i.Id==invoice.Id);invoice.Version=Guid.NewGuid();await db.SaveChangesAsync();old.Version=Guid.NewGuid();var rejected=false;try{await stale.SaveChangesAsync();}catch(DbUpdateConcurrencyException){rejected=true;}Require(rejected);}
            stage="full payment";Require(await Controller().Pay(new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=Guid.NewGuid(),Amount=60,Method="BankTransfer",BankReference="SYN-"+tag}) is RedirectToActionResult);await Flush(c,tx);Require((await db.BillingPayments.Where(p=>p.InvoiceId==invoice.Id).SumAsync(p=>p.ReversesId==null?p.Amount:-p.Amount))==invoice.Total);
            await Reject(c,tx,"UPDATE billing_payments SET \"Amount\"=1 WHERE \"InvoiceId\"=@id",invoice.Id);await Reject(c,tx,"UPDATE billing_invoices SET \"Total\"=1 WHERE \"Id\"=@id",invoice.Id);await Reject(c,tx,"DELETE FROM billing_invoice_lines WHERE \"InvoiceId\"=@id",invoice.Id);
            stage="reversal and void";db.ChangeTracker.Clear();foreach(var original in await db.BillingPayments.Where(p=>p.InvoiceId==invoice.Id&&p.ReversesId==null).ToListAsync()){invoice=await db.BillingInvoices.SingleAsync(i=>i.Id==invoice.Id);Require(await Controller().Reverse(original.Id,Guid.NewGuid(),invoice.Version,"Synthetic correction") is RedirectToActionResult);await Flush(c,tx);db.ChangeTracker.Clear();}
            invoice=await db.BillingInvoices.SingleAsync(i=>i.Id==invoice.Id);Require((await db.BillingPayments.Where(p=>p.InvoiceId==invoice.Id).SumAsync(p=>p.ReversesId==null?p.Amount:-p.Amount))==0m);Require(await db.BillingPayments.CountAsync(p=>p.InvoiceId==invoice.Id)==4);Require(await Controller().Void(invoice.Id,invoice.Version,"Synthetic replacement") is RedirectToActionResult);await Flush(c,tx);Require(invoice.State==InvoiceState.Voided&&invoice.Total==100m&&await db.BillingInvoiceLines.CountAsync(l=>l.InvoiceId==invoice.Id)==1);
            stage="effective pricing and decimal half-cent rounding";var pricedAt=DateTime.UtcNow;db.BillingPrices.AddRange(new BillingPrice{ServiceId=service.Id,UnitPrice=.01m,CreatedAtUtc=pricedAt,EffectiveAtUtc=pricedAt,ActorUserId=admin.Id},new BillingPrice{ServiceId=service.Id,UnitPrice=999m,CreatedAtUtc=pricedAt,EffectiveAtUtc=pricedAt.AddDays(1),ActorUserId=admin.Id});await db.SaveChangesAsync();var rounding=new ChargeForm{PatientId=patient.Id,ServiceId=service.Id,Token=Guid.NewGuid(),Quantity=.5m};Require(await Controller().Charge(rounding) is RedirectToActionResult);await Flush(c,tx);var rounded=await db.PatientCharges.SingleAsync(x=>x.Token==rounding.Token);Require(rounded.UnitPrice==.01m&&rounded.Total==.01m);Require((await db.PatientCharges.SingleAsync(x=>x.Id==charge.Id)).UnitPrice==100m);
            Console.WriteLine("Rollback billing workflow PASS: effective/immutable pricing, LKR half-cent rounding, charge idempotency, draft/issue, partial/full/outstanding balances, duplicate submission, overpayment/actor rejection, stale versions, deferred rollback, immutable ledger/lines/totals, exact full reversals, void and retained history.");
        }
        finally{await tx.RollbackAsync();}
    }
    private static async Task Flush(NpgsqlConnection c,NpgsqlTransaction tx){await using var cmd=new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE; SET CONSTRAINTS ALL DEFERRED",c,tx);await cmd.ExecuteNonQueryAsync();}
    private static async Task Reject(NpgsqlConnection c,NpgsqlTransaction tx,string sql,Guid id){await tx.SaveAsync("billing_guard");try{await using var cmd=new NpgsqlCommand(sql,c,tx);cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();await Flush(c,tx);throw new InvalidOperationException("Expected financial guard rejection.");}catch(PostgresException ex)when(ex.SqlState=="23514"){await tx.RollbackAsync("billing_guard");}await tx.ReleaseAsync("billing_guard");}
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Billing verification failed.");}
}
