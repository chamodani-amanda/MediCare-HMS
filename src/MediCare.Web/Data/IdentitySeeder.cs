using Microsoft.AspNetCore.Identity;

namespace MediCare.Web.Data;

public static class IdentitySeeder
{
    private static readonly string[] Roles =
    {
        "Administrator",
        "Doctor",
        "Nurse",
        "Receptionist",
        "Laboratory Staff",
        "Pharmacist",
        "Accountant"
    };

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration)
    {
        var roleManager = services
            .GetRequiredService<RoleManager<IdentityRole>>();

        var userManager = services
            .GetRequiredService<UserManager<IdentityUser>>();

        // Create the seven hospital roles
        foreach (var roleName in Roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var result = await roleManager.CreateAsync(
                new IdentityRole(roleName));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not create role: {roleName}. " +
                    string.Join("; ",
                        result.Errors.Select(e => e.Description)));
            }
        }

        Console.WriteLine("Hospital roles verified successfully.");

        // Bootstrap only the first administrator
        var existingAdmins = await userManager
            .GetUsersInRoleAsync("Administrator");

        if (existingAdmins.Count > 0)
        {
            Console.WriteLine(
                "An administrator already exists. No new account created.");
            return;
        }

        var email = configuration["BootstrapAdmin:Email"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Administrator email or password is missing.");
        }

        if (await userManager.FindByEmailAsync(email) != null)
        {
            throw new InvalidOperationException(
                "An account already uses that email.");
        }

        var admin = new IdentityUser
        {
            UserName = email,
            Email = email
        };

        var createResult = await userManager
            .CreateAsync(admin, password);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                string.Join("; ",
                    createResult.Errors.Select(e => e.Description)));
        }

        var roleResult = await userManager
            .AddToRoleAsync(admin, "Administrator");

        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(admin);

            throw new InvalidOperationException(
                "Administrator role assignment failed: " +
                string.Join("; ",
                    roleResult.Errors.Select(e => e.Description)));
        }

        Console.WriteLine(
            "Development administrator created successfully.");
    }
}