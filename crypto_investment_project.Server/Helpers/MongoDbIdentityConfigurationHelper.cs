using AspNetCore.Identity.MongoDbCore.Extensions;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using MongoDbGenericRepository;

namespace crypto_investment_project.Server.Helpers
{
    // Custom MongoDbContext that overrides the GUID representation initializer
    public class CustomMongoDbContext : MongoDbContext
    {
        public CustomMongoDbContext(string connectionString, string databaseName)
            : base(connectionString, databaseName)
        {
        }

        // Override to avoid calling the removed BsonDefaults setter.
        protected override void InitializeGuidRepresentation()
        {
            // Optionally register a GUID serializer if needed:
            // BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }
    }

    public static class MongoDbIdentityConfigurationHelper
    {
        public static void Configure(WebApplicationBuilder builder)
        {
            // Retrieve MongoDB settings from configuration.
            var connectionString = builder.Configuration["MongoDB:ConnectionString"];
            var databaseName = builder.Configuration["MongoDB:DatabaseName"];

            // *** Register required services ***

            // 1. Data Protection (required for token providers).
            builder.Services.AddDataProtection();

            // 2. Register System.TimeProvider (available in .NET 7).
            //    This satisfies the dependency for security stamp validation.
            builder.Services.AddSingleton<System.TimeProvider>(System.TimeProvider.System);

            // 3. Register MongoDB.Driver's IMongoClient for your other services.
            var mongoClient = new MongoClient(connectionString);
            builder.Services.AddSingleton<IMongoClient>(mongoClient);

            // Create our custom MongoDB context.
            var mongoDbContext = new CustomMongoDbContext(connectionString, databaseName);

            // Build the Identity configuration.
            var mongoDbIdentityConfig = new MongoDbIdentityConfiguration
            {
                MongoDbSettings = new MongoDbSettings
                {
                    ConnectionString = connectionString,
                    DatabaseName = databaseName
                },
                IdentityOptionsAction = options =>
                {
                    // Password options.
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequiredLength = 6;

                    // Sign-in options.
                    options.SignIn.RequireConfirmedEmail = true;

                    // Lockout options.
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                    options.Lockout.MaxFailedAccessAttempts = 5;

                    // User options.
                    options.User.RequireUniqueEmail = true;
                }
            };

            // Configure Identity to use MongoDB with our custom context.
            builder.Services.ConfigureMongoDbIdentity<ApplicationUser, ApplicationRole, Guid>(
                    mongoDbIdentityConfig,
                    mongoDbContext)
                .AddUserManager<UserManager<ApplicationUser>>()
                .AddSignInManager<SignInManager<ApplicationUser>>()
                .AddRoleManager<RoleManager<ApplicationRole>>()
                .AddDefaultTokenProviders();

            // Optionally, register the custom MongoDbContext for injection elsewhere.
            builder.Services.AddSingleton<CustomMongoDbContext>(mongoDbContext);
        }
    }
}
