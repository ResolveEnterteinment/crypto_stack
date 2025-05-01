using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;

namespace crypto_investment_project.Server.Configuration
{
    public static class RoleInitializationExtensions
    {
        /// <summary>
        /// Creates an extension method to initialize default roles
        /// </summary>
        public static WebApplication InitializeDefaultRoles(this WebApplication app)
        {
            // Create a new scope to resolve required services
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                logger.LogInformation("Initializing default application roles");

                // Get the role manager from DI
                var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

                // Define default system roles
                var defaultRoles = new[]
                {
                    new ApplicationRole
                    {
                        Name = "USER"
                    },
                    new ApplicationRole
                    {
                        Name = "ADMIN"
                    }
                };

                // Check and create each role if needed
                foreach (var role in defaultRoles)
                {
                    if (!roleManager.RoleExistsAsync(role.Name).GetAwaiter().GetResult())
                    {
                        logger.LogInformation("Creating role: {RoleName}", role.Name);

                        var result = roleManager.CreateAsync(role).GetAwaiter().GetResult();

                        if (result.Succeeded)
                        {
                            logger.LogInformation("Successfully created role: {RoleName}", role.Name);
                        }
                        else
                        {
                            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                            logger.LogError("Failed to create role {RoleName}: {Errors}", role.Name, errors);
                        }
                    }
                    else
                    {
                        logger.LogDebug("Role already exists: {RoleName}", role.Name);
                    }
                }

                logger.LogInformation("Role initialization completed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while initializing default roles");
                // Don't rethrow - allow application to continue starting up
            }

            return app;
        }
    }
}