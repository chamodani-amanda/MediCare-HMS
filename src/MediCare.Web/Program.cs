
using MediCare.Web.Services;
using Microsoft.AspNetCore.Identity;
using MediCare.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 1. REGISTER APPLICATION SERVICES
// ==========================================

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<SecurityAuditService>();
builder.Services.AddSingleton<PatientDocumentStorage>();
builder.Services.AddSingleton(TimeProvider.System);


// ==========================================
// 2. DATABASE CONNECTION CONFIGURATION
// ==========================================

// Read the connection string from configuration.
// Local development uses .NET User Secrets.
// Docker / Render can use environment variables.

var connectionString = builder.Configuration
    .GetConnectionString("MediCareDatabase")
    ?? throw new InvalidOperationException(
        "MediCare database connection string is missing.");

// Identify the database specified in the connection string.

var actualDatabase =
    new Npgsql.NpgsqlConnectionStringBuilder(
        connectionString).Database;

// Local development must use medicare_dev.
// Docker testing and cloud hosting must explicitly
// declare the expected database name.

var expectedDatabase = builder.Environment.IsDevelopment()
    ? "medicare_dev"
    : builder.Configuration["MediCare:ExpectedDatabaseName"];

// Stop startup when the database name does not match.

if (string.IsNullOrWhiteSpace(expectedDatabase) ||
    !string.Equals(
        actualDatabase,
        expectedDatabase,
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Database name mismatch or missing expected database configuration. Startup blocked.");
}

// Prevent non-development environments
// from accessing the original development database.

if (!builder.Environment.IsDevelopment() &&
    string.Equals(
        actualDatabase,
        "medicare_dev",
        StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Production must not target medicare_dev.");
}

// Register PostgreSQL and Entity Framework Core.

builder.Services.AddDbContext<MediCareDbContext>(options =>
    options.UseNpgsql(connectionString));


// ==========================================
// 3. ASP.NET CORE IDENTITY
// ==========================================

builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        // Password security
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;

        // Prevent repeated password guessing
        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(15);

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.AllowedForNewUsers = true;

        // Email must be unique
        options.User.RequireUniqueEmail = true;

        // Existing authentication policy
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<MediCareDbContext>()
    .AddDefaultTokenProviders();


// ==========================================
// 4. AUTHENTICATION COOKIE CONFIGURATION
// ==========================================

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// Validate authentication security stamps
// on every request.

builder.Services.Configure<SecurityStampValidatorOptions>(
    options =>
    {
        options.ValidationInterval = TimeSpan.Zero;
    });


// ==========================================
// 5. BUILD WEB APPLICATION
// ==========================================

var app = builder.Build();


// ==========================================
// 6. HTTP REQUEST PIPELINE
// ==========================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    // Enable HTTP Strict Transport Security.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();


// ==========================================
// 7. DEFAULT MVC ROUTES
// ==========================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// ==========================================
// 8. DEVELOPMENT-ONLY DATABASE CHECK
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/database-check",
        async (MediCareDbContext db) =>
        {
            bool connected =
                await db.Database.CanConnectAsync();

            return connected
                ? Results.Ok("Database connection successful!")
                : Results.Problem("Database connection failed.");
        })
        .RequireAuthorization(
            policy => policy.RequireRole("Administrator"));
}


// ==========================================
// 9. DEVELOPMENT-ONLY IDENTITY SEEDING
// ==========================================

if (args.Contains("--seed-identity"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Identity seeding is permitted only in Development.");
    }

    using var scope = app.Services.CreateScope();

    await IdentitySeeder.SeedAsync(
        scope.ServiceProvider,
        app.Configuration);

    return;
}


// ==========================================
// 10. START MEDICARE APPLICATION
// ==========================================

app.Run();
