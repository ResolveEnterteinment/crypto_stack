using AspNetCore.Identity.Mongo;
using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace crypto_investment_project.Server.Configuration;

public static class IdentityExtensions
{
    public static IServiceCollection AddIdentityConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        // Retrieve MongoDB settings from configuration
        var connectionString = configuration["MongoDB:ConnectionString"];
        var databaseName = configuration["MongoDB:DatabaseName"];

        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))
        {
            throw new InvalidOperationException("MongoDB connection string or database name is missing in configuration");
        }

        // Register MongoDB.Driver's IMongoClient
        var mongoClient = new MongoClient(connectionString);
        services.AddSingleton<IMongoClient>(mongoClient);

        // Configure MongoDB Identity
        services.AddIdentityMongoDbProvider<ApplicationUser, ApplicationRole, Guid>(identity =>
        {
            identity.Password.RequiredLength = 4;
            identity.Password.RequireUppercase = false;
            identity.Password.RequireNonAlphanumeric = false;
            identity.Password.RequireLowercase = false;
            // other options
        },
            mongo =>
            {
                mongo.ConnectionString = connectionString;
                mongo.UsersCollection = "users";
                mongo.RolesCollection = "roles";
                // other options
            }
        )
        .AddUserManager<UserManager<ApplicationUser>>()
        .AddSignInManager<SignInManager<ApplicationUser>>()
        .AddRoleManager<RoleManager<ApplicationRole>>()
        .AddDefaultTokenProviders();

        return services;
    }
}