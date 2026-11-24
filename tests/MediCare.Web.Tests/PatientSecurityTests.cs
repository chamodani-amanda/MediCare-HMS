using System.Reflection;
using System.Security.Claims;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace MediCare.Web.Tests;

public class PatientSecurityTests
{
    [Theory]
    [InlineData("Administrator", true, true)]
    [InlineData("Receptionist", true, false)]
    [InlineData("Doctor", false, false)]
    [InlineData("", false, false)]
    public async Task AuthorizationPoliciesEnforcePatientAndAdministratorRoles(string role, bool patientAccess, bool adminAccess)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        using (services)
        {
            var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
            var authorization = services.GetRequiredService<IAuthorizationService>();
            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, string.IsNullOrEmpty(role) ? null : "Test"));
            var controllerAttributes = typeof(PatientsController).GetCustomAttributes<AuthorizeAttribute>();
            var policy = (await AuthorizationPolicy.CombineAsync(provider, controllerAttributes))!;
            Assert.Equal(patientAccess, (await authorization.AuthorizeAsync(user, null, policy)).Succeeded);
            foreach (var method in typeof(PatientsController).GetMethods().Where(m => m.Name is "Archive" or "Reactivate" or "Archived"))
            {
                var combined = (await AuthorizationPolicy.CombineAsync(provider, controllerAttributes.Concat(method.GetCustomAttributes<AuthorizeAttribute>())))!;
                Assert.Equal(adminAccess, (await authorization.AuthorizeAsync(user, null, combined)).Succeeded);
            }
        }
    }

    [Theory]
    [InlineData("report.pdf", "application/pdf", "%PDF-1.7", true)]
    [InlineData("report.pdf.exe", "application/pdf", "%PDF-1.7", false)]
    [InlineData("report.pdf", "text/html", "%PDF-1.7", false)]
    [InlineData("report.pdf", "application/pdf", "<script>", false)]
    [InlineData("report.png", "image/png", "%PDF-1.7", false)]
    [InlineData("report.pdf", "application/pdf", "", false)]
    public void UploadRequiresMatchingExtensionMimeAndSignature(string name, string mime, string data, bool valid)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(data);
        if (valid) Assert.Equal(mime, PatientDocumentStorage.Validate(name, mime, bytes));
        else Assert.Throws<InvalidDataException>(() => PatientDocumentStorage.Validate(name, mime, bytes));
    }

    [Fact]
    public void ValidImagesAndSizeBoundaries()
    {
        Assert.Equal("image/jpeg", PatientDocumentStorage.Validate("image.JPEG", "image/jpeg", [255,216,255]));
        Assert.Equal("image/png", PatientDocumentStorage.Validate("image.png", "image/png", [137,80,78,71,13,10,26,10]));
        var bytes = new byte[PatientDocumentStorage.MaximumSize];
        "%PDF-"u8.CopyTo(bytes);
        Assert.Equal("application/pdf", PatientDocumentStorage.Validate("a.pdf", "application/pdf", bytes));
        Assert.Throws<InvalidDataException>(() => PatientDocumentStorage.Validate("a.pdf", "application/pdf", new byte[bytes.Length + 1]));
    }

    [Fact]
    public void AllPatientEndpointsRequireRolesAndMutationsRequireAntiforgery()
    {
        var type = typeof(PatientsController);
        Assert.Equal("Administrator,Receptionist", type.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<HttpPostAttribute>() != null)
                Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            if (method.Name is "Archive" or "Reactivate" or "Archived")
                Assert.Equal("Administrator", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        }
    }

    private static MediCareDbContext Database(string name) => new(new DbContextOptionsBuilder<MediCareDbContext>()
        .UseInMemoryDatabase(name).Options);
    private static PatientsController Controller(MediCareDbContext db, string role = "Administrator")
    {
        var controller = new PatientsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, role)
        }, "Test"));
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());
        return controller;
    }

    [Fact]
    public async Task ArchiveAndReactivatePreserveIdentityAndDocumentsAndAuditChanges()
    {
        using var db = Database(Guid.NewGuid().ToString());
        var patient = new Patient { PatientNumber = "PAT-TEST", FirstName = "Test", LastName = "Patient" };
        db.Add(patient);
        db.Add(new PatientDocument { PatientId = patient.Id, StoredFileName = Guid.NewGuid().ToString("N") });
        await db.SaveChangesAsync();
        var controller = Controller(db);
        var version = patient.Version;
        Assert.IsType<RedirectToActionResult>(await controller.Archive(patient.Id, version));
        Assert.True(patient.IsArchived);
        Assert.IsType<ConflictObjectResult>(await controller.Reactivate(patient.Id, version));
        Assert.IsType<RedirectToActionResult>(await controller.Reactivate(patient.Id, patient.Version));
        Assert.False(patient.IsArchived);
        Assert.Equal("PAT-TEST", patient.PatientNumber);
        Assert.Single(db.PatientDocuments);
        Assert.Equal(2, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task ProfileUsesRequestedIdAndHidesArchivedPatientFromReceptionist()
    {
        using var db = Database(Guid.NewGuid().ToString());
        var patient = new Patient { FirstName = "Correct", LastName = "Patient", EmergencyContactName = "Contact" };
        db.Add(patient); await db.SaveChangesAsync();
        var controller = Controller(db, "Receptionist");
        var result = Assert.IsType<ViewResult>(await controller.Details(patient.Id));
        var model = Assert.IsType<PatientDetailsViewModel>(result.Model);
        Assert.Equal(patient.Id, model.Id);
        Assert.Equal("Contact", model.EmergencyContactName);
        Assert.IsType<NotFoundResult>(await controller.Details(Guid.NewGuid()));
        patient.IsArchived = true; await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.Details(patient.Id));
        Assert.IsType<NotFoundResult>(await controller.Edit(patient.Id));
        Assert.IsType<ViewResult>(await Controller(db).Details(patient.Id));
        var list = Assert.IsType<PatientListViewModel>(Assert.IsType<ViewResult>(await controller.Index(null)).Model);
        Assert.Empty(list.Patients);
    }

    [Fact]
    public async Task ConflictingDatabaseUpdatesAreRejected()
    {
        var name = Guid.NewGuid().ToString();
        using var first = Database(name); using var second = Database(name);
        first.Add(new Patient()); await first.SaveChangesAsync();
        var a = await first.Patients.SingleAsync();
        var b = await second.Patients.SingleAsync();
        a.Version = Guid.NewGuid(); a.FirstName = "First"; await first.SaveChangesAsync();
        b.Version = Guid.NewGuid(); b.FirstName = "Second";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task StaleEditDoesNotOverwritePatient()
    {
        using var db = Database(Guid.NewGuid().ToString());
        var patient = new Patient { FirstName = "Original" }; db.Add(patient); await db.SaveChangesAsync();
        var controller = Controller(db);
        Assert.IsType<ViewResult>(await controller.Edit(new PatientEditViewModel {
            Id = patient.Id, Version = Guid.NewGuid(), FirstName = "Changed", LastName = "Patient", DateOfBirth = new DateOnly(2000,1,1)
        }));
        Assert.False(controller.ModelState.IsValid);
        Assert.Equal("Original", patient.FirstName);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task DocumentsCannotBeDownloadedThroughAnotherPatient()
    {
        using var db = Database(Guid.NewGuid().ToString());
        var patient = new Patient(); db.Add(patient);
        var document = new PatientDocument { PatientId = patient.Id }; db.Add(document); await db.SaveChangesAsync();
        var storage = new PatientDocumentStorage(new TestEnvironment());
        Assert.IsType<NotFoundResult>(await Controller(db).DownloadDocument(Guid.NewGuid(), document.Id, storage));
        Assert.Throws<InvalidDataException>(() => storage.Open("../../secret"));
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task UploadPersistsPrivateFileMetadataAndAuditAndDownloadIsAttachment()
    {
        var root = Path.Combine(Path.GetTempPath(), "medicare-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var db = Database(Guid.NewGuid().ToString());
            var patient = new Patient(); db.Add(patient); await db.SaveChangesAsync();
            var storage = new PatientDocumentStorage(new TestEnvironment { ContentRootPath = root });
            var controller = Controller(db);
            using var data = new MemoryStream("%PDF-1.7 test"u8.ToArray());
            var file = new FormFile(data, 0, data.Length, "document", "../../private.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            Assert.IsType<RedirectToActionResult>(await controller.UploadDocument(patient.Id, file, storage));
            var metadata = Assert.Single(db.PatientDocuments);
            Assert.Equal("private.pdf", metadata.OriginalFileName);
            Assert.True(Guid.TryParseExact(metadata.StoredFileName, "N", out _));
            Assert.Single(Directory.GetFiles(Path.Combine(root, "App_Data", "PatientDocuments")));
            Assert.False(Directory.Exists(Path.Combine(root, "wwwroot")));
            var download = Assert.IsType<FileStreamResult>(await controller.DownloadDocument(patient.Id, metadata.Id, storage));
            Assert.Equal("private.pdf", download.FileDownloadName);
            Assert.Equal("application/pdf", download.ContentType);
            await download.FileStream.DisposeAsync();
            Assert.Equal(2, await db.SecurityAuditLogs.CountAsync());
            Assert.Equal("no-store", controller.Response.Headers.CacheControl);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DatabaseFailureRemovesUploadedFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "medicare-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var failure = new UploadFailure();
            using var db = new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options);
            var patient = new Patient(); db.Add(patient); await db.SaveChangesAsync();
            failure.Fail = true;
            var storage = new PatientDocumentStorage(new TestEnvironment { ContentRootPath = root });
            using var data = new MemoryStream("%PDF-1.7 test"u8.ToArray());
            var file = new FormFile(data, 0, data.Length, "document", "file.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
            await Assert.ThrowsAsync<DbUpdateException>(() => Controller(db).UploadDocument(patient.Id, file, storage));
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "App_Data", "PatientDocuments")));
            Assert.Empty(await db.PatientDocuments.AsNoTracking().ToListAsync());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class UploadFailure : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new DbUpdateException("Simulated persistence failure");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Fact]
    public async Task RegistrationAndEditingKeepPatientIdentityAndRecordAuditEvents()
    {
        using var db = Database(Guid.NewGuid().ToString());
        var controller = Controller(db, "Receptionist");
        Assert.IsType<RedirectToActionResult>(await controller.Create(new PatientCreateViewModel {
            FirstName = " New ", LastName = " Patient ", DateOfBirth = new DateOnly(2000,1,1), EmergencyContactName = "Contact"
        }));
        var patient = await db.Patients.SingleAsync();
        var number = patient.PatientNumber;
        var id = patient.Id;
        Assert.Equal("New", patient.FirstName);
        Assert.IsType<RedirectToActionResult>(await controller.Edit(new PatientEditViewModel {
            Id = id, Version = patient.Version, FirstName = "Updated", LastName = "Patient", DateOfBirth = patient.DateOfBirth
        }));
        Assert.Equal(id, patient.Id);
        Assert.Equal(number, patient.PatientNumber);
        Assert.Equal("Updated", patient.FirstName);
        Assert.Equal(2, await db.SecurityAuditLogs.CountAsync());
    }

    [Fact]
    public async Task InvalidUploadCreatesNeitherFileNorMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "medicare-tests-" + Guid.NewGuid().ToString("N"));
        using var db = Database(Guid.NewGuid().ToString());
        var patient = new Patient(); db.Add(patient); await db.SaveChangesAsync();
        var controller = Controller(db);
        using var data = new MemoryStream("<script>"u8.ToArray());
        var file = new FormFile(data, 0, data.Length, "document", "fake.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        Assert.IsType<RedirectToActionResult>(await controller.UploadDocument(patient.Id, file,
            new PatientDocumentStorage(new TestEnvironment { ContentRootPath = root })));
        Assert.NotNull(controller.TempData["DocumentError"]);
        Assert.False(Directory.Exists(root));
        Assert.Empty(db.PatientDocuments);
    }
}
