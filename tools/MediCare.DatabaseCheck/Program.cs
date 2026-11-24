using Microsoft.Extensions.Configuration;
using Npgsql;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

// Application is available only through explicitly named, approved migration commands.
if(args.Contains("--apply-approved-billing")||args.Contains("--billing-preflight")||args.Contains("--verify-billing")||args.Contains("--integration-billing"))
{
    await BillingDatabaseChecks.Run(args);
    return;
}
if (args.Contains("--apply-approved-stay") || args.Contains("--verify-stay") || args.Contains("--integration-stay"))
{
    await HospitalStayDatabaseChecks.Run(args);
    return;
}
if (args.Contains("--apply-approved-pharmacy") || args.Contains("--pharmacy-preflight") || args.Contains("--verify-pharmacy") || args.Contains("--integration-pharmacy"))
{
    await PharmacyChecks.Run(args);
    return;
}
if (args.Contains("--apply-approved-lab") || args.Contains("--verify-lab") || args.Contains("--integration-lab"))
{
    await LaboratoryDatabaseChecks.Run(args);
    return;
}

if (args.Contains("--apply-approved-emr") || args.Contains("--verify-emr") || args.Contains("--integration-emr"))
{
    await MedicalRecordDatabaseChecks.Run(args);
    return;
}

// Default probe is read-only; never starts the web app or seeds Identity.
try
{
    var root = Path.GetFullPath("src/MediCare.Web");
    var config = new ConfigurationBuilder().SetBasePath(root)
        .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
        .AddUserSecrets("ab9496e4-0bbb-4c90-a4de-7999722897c0")
        .AddEnvironmentVariables().Build();
    var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("MediCareDatabase"));
    if (connection.Database != "medicare_dev")
        throw new InvalidOperationException("Configured target is not medicare_dev; connection refused.");
    connection.Timeout = 5;
    await using var db = new NpgsqlConnection(connection.ConnectionString);
    await db.OpenAsync();
    await using var transaction = await db.BeginTransactionAsync();
    await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", db, transaction))
        await readOnly.ExecuteNonQueryAsync();
    await using var check = new NpgsqlCommand("SELECT current_database()", db, transaction);
    if ((string?)await check.ExecuteScalarAsync() != "medicare_dev") throw new InvalidOperationException();
    Console.WriteLine("Read-only PostgreSQL connection verified: medicare_dev.");
    await using var migrations = new NpgsqlCommand("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"", db, transaction);
    await using var reader = await migrations.ExecuteReaderAsync();
    while (await reader.ReadAsync()) Console.WriteLine("Applied migration: " + reader.GetString(0));
    await reader.DisposeAsync();

    await using var context = new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql(db).Options);
    await context.Database.UseTransactionAsync(transaction);
    var controller = new PatientsController(context)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "read-only-verification"),
                new Claim(ClaimTypes.Role, "Administrator") }, "Verification"))
        } }
    };
    if (await controller.Index("PAT-") is not ViewResult || await controller.Archived() is not ViewResult)
        throw new InvalidOperationException("Patient list queries failed.");
    var patientId = await context.Patients.Where(p => !p.IsArchived).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
    if (patientId.HasValue && (await controller.Details(patientId.Value) is not ViewResult || await controller.Edit(patientId.Value) is not ViewResult))
        throw new InvalidOperationException("Patient profile/edit queries failed.");
    // Read the new document mapping even if there are currently no documents.
    await context.PatientDocuments.AsNoTracking().Take(1).ToListAsync();
    Console.WriteLine("Read-only PostgreSQL patient search, archive listing, profile/edit and document mapping checks: PASS.");
    foreach (var pending in await context.Database.GetPendingMigrationsAsync()) Console.WriteLine("Pending migration (not applied): " + pending);
    if (args.Contains("--emr-preflight")) await MedicalRecordPreflight.Verify(context, db, transaction);
    if (args.Contains("--lab-preflight")) await LaboratoryPreflight.Verify(context, db, transaction);
    if (args.Contains("--stay-preflight")) await HospitalStayPreflight.Verify(context, db, transaction);
    await using var schema = new NpgsqlCommand("SELECT table_name, count(*) FROM information_schema.columns WHERE table_schema='public' GROUP BY table_name ORDER BY table_name", db, transaction);
    await using var schemaReader = await schema.ExecuteReaderAsync();
    while (await schemaReader.ReadAsync()) Console.WriteLine("Schema table: " + schemaReader.GetString(0) + "; columns=" + schemaReader.GetInt64(1));
    await schemaReader.DisposeAsync();
    await using var extension = new NpgsqlCommand("SELECT default_version, installed_version, has_database_privilege(current_user,current_database(),'CREATE') FROM pg_available_extensions WHERE name='btree_gist'", db, transaction);
    await using var extensionReader = await extension.ExecuteReaderAsync();
    if (await extensionReader.ReadAsync()) Console.WriteLine("btree_gist available=" + extensionReader.GetString(0) + "; installed=" + !extensionReader.IsDBNull(1) + "; database CREATE privilege=" + extensionReader.GetBoolean(2));
    else Console.WriteLine("btree_gist is not available on this PostgreSQL installation.");
}
catch (Exception ex)
{
    // Deliberately omit exception messages because provider errors can contain sensitive information.
    Console.WriteLine("Database verification failed (" + ex.GetType().Name + "). No secrets printed.");
    Environment.ExitCode = 1;
}
