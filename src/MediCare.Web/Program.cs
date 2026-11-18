using MediCare.Web.Services;
using Microsoft.AspNetCore.Identity;
using MediCare.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<SecurityAuditService>();
var connectionString = builder.Configuration
    .GetConnectionString("MediCareDatabase")
    ?? throw new InvalidOperationException(
        "MediCare database connection string is missing.");

builder.Services.AddDbContext<MediCareDbContext>(options =>
    options.UseNpgsql(connectionString));

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

        // Development only: no email service yet
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<MediCareDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";

    options.ExpireTimeSpan = TimeSpan.FromMinutes(20);
    options.SlidingExpiration = true;

    options.Cookie.HttpOnly = true;

    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
// Validate authentication security stamps on each request.
// Suitable for our local development security tests.
builder.Services.Configure<SecurityStampValidatorOptions>(
    options =>
    {
        options.ValidationInterval = TimeSpan.Zero;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

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
        });
}

// One-time local development setup only
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

app.Run();
