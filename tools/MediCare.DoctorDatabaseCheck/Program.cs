using System.Diagnostics;
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
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Npgsql;

const string target = "20261118184537_AddDoctorManagement";
const string previous = "20261118171711_AddPatientDocumentsAndConcurrency";
var stage = "configuration";
try
{
    var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/MediCare.Web"))
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true)
        .AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0").AddEnvironmentVariables().Build();
    var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));
    Require(cs.Database == "medicare_dev", "Configured database mismatch; connection refused."); cs.Timeout = 10;
    await using var connection = new NpgsqlConnection(cs.ConnectionString); await connection.OpenAsync();
    await using (var cmd = new NpgsqlCommand("SELECT current_database()", connection))
        Require((string?)await cmd.ExecuteScalarAsync() == "medicare_dev", "Actual database mismatch.");
    Console.WriteLine("Actual connected database: medicare_dev.");
    await using var db = Context(connection);
    var before = await Snapshot(connection);
    Require(before.ContainsKey("patients") && before.Keys.Count(t => t.StartsWith("AspNet", StringComparison.Ordinal)) == 7, "Patient/Identity tables missing.");
    if (args.Contains("--apply-approved"))
    {
        stage = "backup verification";
        const string backup = @"D:\MediCare-Backups\medicare_dev_before_step7_20261118_184105_5c6cb84e.backup";
        await using (var stream = File.OpenRead(backup))
            Require(stream.Length == 24415 && Convert.ToHexString(await SHA256.HashDataAsync(stream)) == "FDB64611627EF54E664A671C6B5B16314E2F74A5A6BC9A7B9037B3BBE0935900", "Verified backup changed.");
        var listing = await Archive("--list", backup);
        Require(listing.Contains("dbname: medicare_dev") && listing.Contains("TABLE DATA"), "Unexpected backup contents.");
        await Archive("--file=NUL", backup);
        Console.WriteLine("Backup PASS: 24,415 bytes; SHA-256 unchanged; pg_restore --list and full offline read passed. No database restore.");
        stage = "migration safety review";
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[target], db.Database.ProviderName!);
        Require(migration.UpOperations.Count == 10 && migration.UpOperations.All(op => op is CreateTableOperation or CreateIndexOperation), "Unexpected migration operations.");
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToList();
        Require(tables.Select(t => t.Name).Order().SequenceEqual(new[] { "departments", "doctor_schedules", "doctors" }), "Unexpected migration tables.");
        Require(tables.SelectMany(t => t.ForeignKeys).All(fk => fk.OnDelete == ReferentialAction.Restrict), "Unexpected foreign-key delete behavior.");
        string Normalize(string value) => value.Replace("\r\n", "\n").Trim();
        var migrator = db.GetService<IMigrator>();
        Require(Normalize(migrator.GenerateScript(previous, target)) == Normalize(await File.ReadAllTextAsync("tools/DoctorManagement-migration.sql")), "Migration SQL differs from reviewed script.");
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        Require(pending.SequenceEqual(new[] { target }), "Unexpected pending migrations; stopped before application.");
        Console.WriteLine("Migration review PASS: only the approved additive migration is pending; generated SQL matches reviewed script.");
        stage = "approved migration application";
        await migrator.MigrateAsync(target);
        Console.WriteLine("Applied migration: " + target);
    }
    stage = "schema verification";
    Require((await db.Database.GetAppliedMigrationsAsync()).Contains(target) && !(await db.Database.GetPendingMigrationsAsync()).Any(), "Unexpected migration history.");
    await VerifySchema(connection, db);
    await Preserved(connection, before);
    if (args.Contains("--integration"))
    {
        stage = "rollback-only PostgreSQL integration checks";
        await Integration(connection);
        await Preserved(connection, before);
    }
    Console.WriteLine("SUCCESS: schema/data verification complete; no pending migrations.");
}
catch (Exception ex)
{
    Console.WriteLine("STOPPED during " + stage + ": " + ex.GetType().Name +
        (ex is CheckFailure ? "; " + ex.Message : ex is PostgresException pg ? "; SQLSTATE=" + pg.SqlState : "") + ". Provider details suppressed to protect secrets.");
    Environment.ExitCode = 1;
}

static void Require(bool valid, string message) { if (!valid) throw new CheckFailure(message); }
static MediCareDbContext Context(NpgsqlConnection connection) => new(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql(connection).Options);
static async Task<string> Archive(string option, string path)
{
    var info = new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe")
    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    info.ArgumentList.Add(option); info.ArgumentList.Add(path);
    using var process = Process.Start(info) ?? throw new CheckFailure("Cannot start pg_restore.");
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync(); var text = await output; await error;
    Require(process.ExitCode == 0, "Archive validation failed."); return text;
}

static async Task VerifySchema(NpgsqlConnection connection, MediCareDbContext db)
{
    foreach (var table in new[] { "departments", "doctors", "doctor_schedules" })
    {
        var entity = db.Model.GetEntityTypes().Single(t => t.GetTableName() == table);
        await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema='public' AND table_name=@table", connection))
        { cmd.Parameters.AddWithValue("table", table); Require((long)(await cmd.ExecuteScalarAsync())! == entity.GetProperties().Count(), "Unexpected columns: " + table); }
        foreach (var index in entity.GetIndexes())
        {
            await using var cmd = new NpgsqlCommand("SELECT i.indisunique, i.indisvalid, pg_get_expr(i.indpred,i.indrelid) FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname=@name", connection);
            cmd.Parameters.AddWithValue("name", index.GetDatabaseName()!);
            await using var reader = await cmd.ExecuteReaderAsync();
            Require(await reader.ReadAsync() && reader.GetBoolean(0) == index.IsUnique && reader.GetBoolean(1), "Index check failed: " + index.GetDatabaseName());
            if (index.GetFilter() != null) Require(!reader.IsDBNull(2), "Missing filtered schedule index.");
        }
        await using (var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_constraint WHERE conrelid=('public.' || @table)::regclass AND contype='p' AND convalidated", connection))
        { cmd.Parameters.AddWithValue("table", table); Require((long)(await cmd.ExecuteScalarAsync())! == 1, "Missing primary key."); }
        Console.WriteLine("Schema PASS: " + table + "; columns, primary key and all expected indexes verified.");
    }
    var checks = new Dictionary<string,string>
    {
        ["doctor Identity FK"] = "SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.doctors'::regclass AND confrelid='public.\"AspNetUsers\"'::regclass AND conname='FK_doctors_AspNetUsers_IdentityUserId' AND contype='f' AND confdeltype='r' AND convalidated)",
        ["doctor department FK"] = "SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.doctors'::regclass AND confrelid='public.departments'::regclass AND conname='FK_doctors_departments_DepartmentId' AND contype='f' AND confdeltype='r' AND convalidated)",
        ["schedule doctor FK"] = "SELECT EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='public.doctor_schedules'::regclass AND confrelid='public.doctors'::regclass AND conname='FK_doctor_schedules_doctors_DoctorId' AND contype='f' AND confdeltype='r' AND convalidated)",
        ["schedule check constraints"] = "SELECT count(*)=2 FROM pg_constraint WHERE conrelid='public.doctor_schedules'::regclass AND contype='c' AND convalidated AND conname IN ('CK_doctor_schedules_day','CK_doctor_schedules_times')",
        ["concurrency columns"] = "SELECT count(*)=3 FROM information_schema.columns WHERE table_schema='public' AND table_name IN ('doctors','departments','doctor_schedules') AND column_name='Version' AND udt_name='uuid' AND is_nullable='NO'"
    };
    foreach (var check in checks)
    {
        await using var cmd = new NpgsqlCommand(check.Value, connection);
        Require((bool?)await cmd.ExecuteScalarAsync() == true, "Schema check failed: " + check.Key);
        Console.WriteLine("Schema PASS: " + check.Key);
    }
}

static async Task<Dictionary<string,(long Count,string Hash)>> Snapshot(NpgsqlConnection connection)
{
    var tables = new List<string>();
    await using (var cmd = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename <> '__EFMigrationsHistory' ORDER BY tablename", connection))
    await using (var reader = await cmd.ExecuteReaderAsync()) while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
    var result = new Dictionary<string,(long,string)>();
    foreach (var table in tables)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long count = 0;
        var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
        await using var cmd = new NpgsqlCommand("SELECT row_to_json(t)::text FROM public." + quoted + " t ORDER BY row_to_json(t)::text COLLATE \"C\"", connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { hash.AppendData(Encoding.UTF8.GetBytes(reader.GetString(0) + "\n")); count++; }
        result[table] = (count, Convert.ToHexString(hash.GetHashAndReset()));
    }
    return result;
}
static async Task Preserved(NpgsqlConnection connection, Dictionary<string,(long Count,string Hash)> before)
{
    var after = await Snapshot(connection);
    foreach (var pair in before)
    {
        Require(after.TryGetValue(pair.Key, out var value) && value == pair.Value, "Existing data changed: " + pair.Key);
        Console.WriteLine("Preserved: " + pair.Key + "; rows=" + pair.Value.Count + "; full-row fingerprints unchanged.");
    }
}

static T Controller<T>(T controller) where T : Controller
{
    controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "rollback-only-integration"), new Claim(ClaimTypes.Role, "Administrator") }, "Integration"))
    } }; return controller;
}

static async Task Integration(NpgsqlConnection connection)
{
    await using var tx = await connection.BeginTransactionAsync();
    try
    {
        await using var db = Context(connection); await db.Database.UseTransactionAsync(tx);
        var prefix = "CHECK-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        var departmentController = Controller(new DepartmentsController(db));
        Require(await departmentController.Create(new DepartmentFormViewModel { Name = prefix }) is RedirectToActionResult, "Department registration failed.");
        var department = await db.Departments.SingleAsync(d => d.NormalizedName == prefix);
        var role = await db.Roles.SingleAsync(r => r.Name == "Doctor");
        var user = new IdentityUser { Id = Guid.NewGuid().ToString(), UserName = prefix, NormalizedUserName = prefix, LockoutEnabled = true };
        db.Users.Add(user); db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id }); await db.SaveChangesAsync();
        DoctorFormViewModel Fields(string registration, string? link = null) => new() { RegistrationNumber = registration, FirstName = "Integration", LastName = "Doctor", Qualifications = "Test qualification", Specialization = "Test specialization", DepartmentId = department.Id, IdentityUserId = link };
        var registration = Controller(new DoctorsController(db));
        Require(await registration.Create(Fields(prefix, user.Id)) is RedirectToActionResult, "Doctor registration/linking failed.");
        var doctor = await db.Doctors.SingleAsync(d => d.RegistrationNumber == prefix);
        Require(doctor.IdentityUserId == user.Id, "Identity link not stored.");
        var duplicate = Controller(new DoctorsController(db));
        Require(await duplicate.Create(Fields(prefix)) is ViewResult && !duplicate.ModelState.IsValid, "Duplicate registration accepted.");
        var linked = Controller(new DoctorsController(db));
        Require(await linked.Create(Fields(prefix + "-L", user.Id)) is ViewResult && !linked.ModelState.IsValid, "Duplicate Identity link accepted.");
        Require(await Controller(new DoctorsController(db)).Create(Fields(prefix + "-2")) is RedirectToActionResult, "Second test doctor creation failed.");
        var second = await db.Doctors.SingleAsync(d => d.RegistrationNumber == prefix + "-2");
        Console.WriteLine("Integration PASS: doctor registration, duplicate rejection and unique Doctor-role account linking.");

        DoctorScheduleFormViewModel Shift(int from, int until) => new() { DoctorId = doctor.Id, DoctorVersion = doctor.Version, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(from,0), EndTime = new TimeOnly(until,0) };
        Require(await Controller(new DoctorSchedulesController(db)).Create(Shift(9,12)) is RedirectToActionResult, "Valid schedule failed.");
        var overlap = Controller(new DoctorSchedulesController(db));
        Require(await overlap.Create(Shift(10,11)) is ViewResult && overlap.ModelState.Values.SelectMany(v => v.Errors).Any(e => e.ErrorMessage.Contains("overlaps")), "Overlap accepted.");
        Require(await Controller(new DoctorSchedulesController(db)).Create(Shift(12,13)) is RedirectToActionResult, "Adjacent schedule rejected.");
        Console.WriteLine("Integration PASS: PostgreSQL overlap query rejects intersecting shifts and accepts adjacent shifts.");

        // Two EF contexts share this rollback-only PostgreSQL transaction. The second
        // tracks an old version; a savepoint isolates its failed optimistic update.
        await using (var competing = Context(connection))
        {
            await competing.Database.UseTransactionAsync(tx);
            var stale = await competing.Doctors.SingleAsync(d => d.Id == doctor.Id);
            var original = stale.Version;
            var first = Fields(prefix, user.Id); first.Id = doctor.Id; first.Version = doctor.Version; first.FirstName = "First writer";
            Require(await Controller(new DoctorsController(db)).Edit(first) is RedirectToActionResult, "First edit failed.");
            var secondEdit = Fields(prefix, user.Id); secondEdit.Id = doctor.Id; secondEdit.Version = original; secondEdit.FirstName = "Stale writer";
            var writer = Controller(new DoctorsController(competing));
            Require(await writer.Edit(secondEdit) is ViewResult && writer.ModelState.Values.SelectMany(v => v.Errors).Any(e => e.ErrorMessage.Contains("while saving")), "PostgreSQL stale update not rejected.");
            await using var fresh = Context(connection); await fresh.Database.UseTransactionAsync(tx);
            Require((await fresh.Doctors.SingleAsync(d => d.Id == doctor.Id)).FirstName == "First writer", "Stale update overwrote first writer.");
        }
        Console.WriteLine("Integration PASS: real PostgreSQL optimistic concurrency rejects stale doctor saves without overwriting.");

        await using (var competing = Context(connection))
        {
            await competing.Database.UseTransactionAsync(tx);
            var stale = await competing.Doctors.SingleAsync(d => d.Id == doctor.Id);
            var version = stale.Version;
            Require(await Controller(new DoctorSchedulesController(db)).Create(Shift(14,15)) is RedirectToActionResult, "First competing schedule failed.");
            var auditCount = await db.SecurityAuditLogs.CountAsync();
            var writer = Controller(new DoctorSchedulesController(competing));
            var staleShift = new DoctorScheduleFormViewModel { DoctorId = doctor.Id, DoctorVersion = version, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(15,0), EndTime = new TimeOnly(16,0) };
            Require(await writer.Create(staleShift) is ViewResult && writer.ModelState.Values.SelectMany(v => v.Errors).Any(e => e.ErrorMessage.Contains("while saving")), "Schedule aggregate concurrency check failed.");
            Require(!await db.DoctorSchedules.AnyAsync(s => s.DoctorId == doctor.Id && s.StartTime == new TimeOnly(15,0)) && await db.SecurityAuditLogs.CountAsync() == auditCount,
                "Failed schedule writer left a schedule or audit event behind.");
        }
        Console.WriteLine("Integration PASS: competing schedule writer rejected by doctor-version check; failed schedule and audit write rolled back atomically.");

        var scheduleId = await db.DoctorSchedules.Where(s => s.DoctorId == doctor.Id).Select(s => s.Id).FirstAsync();
        var adjacentId = await db.DoctorSchedules.Where(s => s.DoctorId == doctor.Id && s.StartTime == new TimeOnly(12,0)).Select(s => s.Id).SingleAsync();
        var otherDepartment = new Department { Name = prefix + "-ALT", NormalizedName = prefix + "-ALT" };
        db.Departments.Add(otherDepartment); await db.SaveChangesAsync();
        await Constraint(connection, tx, "UPDATE departments SET \"NormalizedName\"=@value WHERE \"Id\"=@id", "23505", otherDepartment.Id, prefix);
        await Constraint(connection, tx, "UPDATE doctors SET \"RegistrationNumber\"=@value WHERE \"Id\"=@id", "23505", second.Id, prefix);
        await Constraint(connection, tx, "UPDATE doctors SET \"IdentityUserId\"=@value WHERE \"Id\"=@id", "23505", second.Id, user.Id);
        await Constraint(connection, tx, "UPDATE doctors SET \"DepartmentId\"=@value::uuid WHERE \"Id\"=@id", "23503", second.Id, Guid.NewGuid().ToString());
        await Constraint(connection, tx, "UPDATE doctor_schedules SET \"DayOfWeek\"=99 WHERE \"Id\"=@id", "23514", scheduleId);
        await Constraint(connection, tx, "UPDATE doctor_schedules SET \"EndTime\"=\"StartTime\" WHERE \"Id\"=@id", "23514", scheduleId);
        await Constraint(connection, tx, "UPDATE doctor_schedules SET \"DoctorId\"=@value::uuid WHERE \"Id\"=@id", "23503", scheduleId, Guid.NewGuid().ToString());
        await Constraint(connection, tx, "UPDATE doctor_schedules SET \"StartTime\"='09:00'::time WHERE \"Id\"=@id", "23505", adjacentId);
        Console.WriteLine("Integration PASS: PostgreSQL unique department/registration/account/active-shift indexes, relationship FKs and schedule day/time constraints reject invalid data.");
    }
    finally { await tx.RollbackAsync(); Console.WriteLine("Integration transaction rolled back: no test doctors, departments, schedules, Identity users or audit events retained."); }
}

static async Task Constraint(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, string expected, Guid id, string? value = null, bool expectError = true)
{
    await tx.SaveAsync("constraint_check"); var rejected = false;
    try
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx); cmd.Parameters.AddWithValue("id", id);
        if (value != null) cmd.Parameters.AddWithValue("value", value);
        await cmd.ExecuteNonQueryAsync();
    }
    catch (PostgresException ex) when (ex.SqlState == expected) { rejected = true; }
    finally { await tx.RollbackAsync("constraint_check"); await tx.ReleaseAsync("constraint_check"); }
    Require(rejected == expectError, "Unexpected constraint outcome; SQLSTATE expected " + expected);
}

sealed class CheckFailure(string message) : Exception(message);
