using System.Diagnostics;
using System.Security.Cryptography;
using MediCare.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

internal static class LaboratoryPreflight
{
    internal const string Target="20261119053856_AddLaboratoryManagement";
    internal static async Task Verify(MediCareDbContext db,NpgsqlConnection connection,NpgsqlTransaction tx)
    {
        Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));
        var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[Target],db.Database.ProviderName!);
        var names=new[]{"lab_tests","lab_requests","lab_result_revisions"};var tables=migration.UpOperations.OfType<CreateTableOperation>().ToList();
        Require(tables.Count==3 && tables.All(t=>names.Contains(t.Name)));
        Require(migration.UpOperations.All(o=>o is CreateTableOperation or CreateIndexOperation or SqlOperation));
        Require(tables.SelectMany(t=>t.ForeignKeys).Count()==9 && tables.SelectMany(t=>t.ForeignKeys).All(f=>f.OnDelete==ReferentialAction.Restrict));
        Require(tables.Sum(t=>t.CheckConstraints.Count)==14 && migration.UpOperations.OfType<CreateIndexOperation>().All(i=>names.Contains(i.Table)));
        var sql=migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        Require(!sql.Contains("DROP ",StringComparison.OrdinalIgnoreCase) && !sql.Contains("TRUNCATE",StringComparison.OrdinalIgnoreCase));
        Require(sql.Contains("Finalized laboratory results are immutable") && sql.Contains("DEFERRABLE INITIALLY DEFERRED") && sql.Contains("Recorded sample custody is immutable"));
        static string Normalize(string text)=>text.Replace("\r\n","\n").Trim();
        Require(Normalize(db.GetService<IMigrator>().GenerateScript("20261119043722_AddMedicalRecords",Target))==Normalize(await File.ReadAllTextAsync("tools/LaboratoryManagement-migration.sql")));
        await using(var cmd=new NpgsqlCommand("""
            SELECT NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND relname IN ('lab_tests','lab_requests','lab_result_revisions'))
            AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND proname IN ('medicare_lab_request_guard','medicare_lab_result_guard','medicare_lab_completion_check'))
            """,connection,tx))Require((bool?)await cmd.ExecuteScalarAsync()==true);
        Console.WriteLine("Laboratory preflight PASS: sole pending migration, three new tables, nine restrictive foreign keys, fourteen checks, new-table indexes/guards, no object-name collisions; generated SQL matches reviewed migration.");
        const string path=@"D:\MediCare-Backups\medicare_dev_before_step7_20261119_054513_4e010026.backup";
        await using(var stream=File.OpenRead(path))Require(stream.Length==56284 && Convert.ToHexString(await SHA256.HashDataAsync(stream))=="F0AF62E77F1F22DC4BAB8398774283461AAE13333791EDCBCFE95DEC635B1EEF");
        var listing=await Archive("--list",path);Require(listing.Contains("dbname: medicare_dev") && listing.Contains("TABLE DATA"));await Archive("--file=NUL",path);
        Console.WriteLine("Laboratory backup PASS: 56,284 bytes; unchanged SHA-256; archive list and full offline read passed. No restore or migration applied.");
    }
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Laboratory preflight failed; migration application is not permitted.");}
    private static async Task<string> Archive(string option,string path)
    {
        var info=new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.ArgumentList.Add(option);info.ArgumentList.Add(path);using var p=Process.Start(info)??throw new InvalidOperationException("Cannot verify backup.");
        var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await error;Require(p.ExitCode==0);return await output;
    }
}
