using System.Diagnostics;
using System.Security.Cryptography;
using MediCare.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

internal static class HospitalStayPreflight
{
    internal const string Target="20261119090005_AddInpatientAndOutpatientManagement";
    internal const string Backup=@"D:\MediCare-Backups\medicare_dev_before_step7_20261119_085915_e9f4cda4.backup";
    internal const string Checksum="ABC138D97A0653F18DED2CBFD961DD39118A2045C46129DCAF007B0135AB33C9";
    internal static async Task Verify(MediCareDbContext db,NpgsqlConnection connection,NpgsqlTransaction tx)
    {
        var before=await MedicalRecordDatabaseChecks.Snapshot(connection);
        Require((await db.Database.GetPendingMigrationsAsync()).SequenceEqual(new[]{Target}));
        var assembly=db.GetService<IMigrationsAssembly>();var migration=assembly.CreateMigration(assembly.Migrations[Target],db.Database.ProviderName!);
        Require(migration.UpOperations.All(o=>o is CreateTableOperation or CreateIndexOperation or SqlOperation));
        var names=new[]{"admission_clinical_notes","admissions","bed_occupancies","bed_transfers","beds","outpatient_clinical_notes","outpatient_visits","wards"};
        Require(migration.UpOperations.OfType<CreateTableOperation>().Select(t=>t.Name).OrderBy(n=>n).SequenceEqual(names));
        Require(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys).All(f=>f.OnDelete==ReferentialAction.Restrict));
        Require(migration.UpOperations.OfType<CreateIndexOperation>().All(i=>names.Contains(i.Table)));
        var sql=migration.UpOperations.OfType<SqlOperation>().Single().Sql;Require(!sql.Contains("DROP ",StringComparison.OrdinalIgnoreCase)&&!sql.Contains("TRUNCATE",StringComparison.OrdinalIgnoreCase)&&!sql.Contains("DELETE FROM",StringComparison.OrdinalIgnoreCase));Require(sql.Contains("EXCLUDE USING gist")&&sql.Contains("DEFERRABLE INITIALLY DEFERRED")&&sql.Contains("FOR UPDATE"));
        static string Normalize(string value)=>value.Replace("\r\n","\n").Trim();Require(Normalize(db.GetService<IMigrator>().GenerateScript("20261119072825_AddPharmacyManagement",Target))==Normalize(await File.ReadAllTextAsync("tools/HospitalStay-migration.sql")));
        await using(var cmd=new NpgsqlCommand("SELECT current_database()='medicare_dev' AND EXISTS(SELECT 1 FROM pg_extension WHERE extname='btree_gist') AND NOT EXISTS(SELECT 1 FROM pg_class WHERE relnamespace='public'::regnamespace AND relname=ANY(@names)) AND NOT EXISTS(SELECT 1 FROM pg_proc WHERE pronamespace='public'::regnamespace AND (proname LIKE 'medicare_stay_%' OR proname IN ('medicare_admission_guard','medicare_occupancy_guard','medicare_transfer_guard','medicare_outpatient_guard')))",connection,tx)){cmd.Parameters.AddWithValue("names",names);Require((bool?)await cmd.ExecuteScalarAsync()==true);}
        await using(var stream=File.OpenRead(Backup))Require(stream.Length==118841&&Convert.ToHexString(await SHA256.HashDataAsync(stream))==Checksum);
        foreach(var option in new[]{"--list","--file=NUL"})
        {
            var info=new ProcessStartInfo(@"C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};info.ArgumentList.Add(option);info.ArgumentList.Add(Backup);using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var error=p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();await error;Require(p.ExitCode==0);var listing=await output;if(option=="--list")Require(listing.Contains("dbname: medicare_dev")&&listing.Contains("TABLE DATA"));
        }
        Console.WriteLine("Stay preflight PASS: medicare_dev; sole pending additive migration; eight new tables with restrictive FKs; occupancy exclusion and history guards; existing btree_gist; SQL artifact matches; no object collisions or existing-table mutations.");
        Console.WriteLine("Backup PASS: 118,841 bytes; unchanged SHA-256; pg_restore list and complete offline archive read passed. No migration applied.");
        await MedicalRecordDatabaseChecks.Preserved(connection,before);
    }
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Hospital stay preflight failed.");}
}
