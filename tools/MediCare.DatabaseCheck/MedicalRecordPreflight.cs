using System.Diagnostics;
using System.Security.Cryptography;
using MediCare.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

internal static class MedicalRecordPreflight
{
    internal const string Migration = "20261119043722_AddMedicalRecords";
    internal static async Task Verify(MediCareDbContext db, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[] { Migration }));
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[Migration], db.Database.ProviderName!);
        var expected = new[] { "medical_records", "medical_record_revisions", "medical_prescriptions" };
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Require(tables.Length == 3 && tables.All(t => expected.Contains(t.Name)));
        Require(migration.UpOperations.All(o => o is CreateTableOperation or CreateIndexOperation or SqlOperation));
        Require(tables.SelectMany(t => t.ForeignKeys).Count() == 7 && tables.SelectMany(t => t.ForeignKeys).All(f => f.OnDelete == ReferentialAction.Restrict));
        Require(migration.UpOperations.OfType<CreateIndexOperation>().All(i => expected.Contains(i.Table)));
        var guards = migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        Require(!guards.Contains("DROP ", StringComparison.OrdinalIgnoreCase) && !guards.Contains("TRUNCATE", StringComparison.OrdinalIgnoreCase));
        Require(guards.Contains("CREATE TRIGGER medicare_emr_prescription_guard") && guards.Contains("Finalized medical revisions are immutable"));
        static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
        Require(Normalize(db.GetService<IMigrator>().GenerateScript("20261119030236_AddAppointmentManagement", Migration)) == Normalize(await File.ReadAllTextAsync("tools/MedicalRecords-migration.sql")));
        await using (var command = new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND relname IN ('medical_records','medical_record_revisions','medical_prescriptions'))
            AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname IN ('medicare_emr_record_guard','medicare_emr_revision_guard','medicare_emr_prescription_guard'))
            """, connection, transaction))
            Require((bool?)await command.ExecuteScalarAsync() == true);
        Console.WriteLine("EMR preflight PASS: sole pending migration; three new tables, seven restrictive foreign keys, new-table indexes/guards; no object-name collisions; forward SQL matches reviewed migration.");
        const string path = @"D:\MediCare-Backups\medicare_dev_before_step7_20261119_044205_abe286b5.backup";
        await using (var stream = File.OpenRead(path))
            Require(stream.Length == 38123 && Convert.ToHexString(await SHA256.HashDataAsync(stream)) == "D880A5A084B1B0EB59A0F436DA13F63C44125AC54EC79172488AB897F85546D7");
        var listing = await Archive("--list", path);
        Require(listing.Contains("dbname: medicare_dev") && listing.Contains("TABLE DATA"));
        await Archive("--file=NUL", path);
        Console.WriteLine("EMR backup PASS: file size and SHA-256 unchanged; pg_restore archive listing and full offline read passed. No restore or migration application performed.");
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("EMR preflight failed; no database changes permitted."); }
    private static async Task<string> Archive(string option, string path)
    {
        var info = new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(option); info.ArgumentList.Add(path);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot verify backup.");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await error; Require(process.ExitCode == 0); return await output;
    }
}
