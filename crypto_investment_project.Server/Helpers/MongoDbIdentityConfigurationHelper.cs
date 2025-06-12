using AspNetCore.Identity.Mongo;
using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;

namespace crypto_investment_project.Server.Helpers
{
    public static class MongoDbIdentityConfigurationHelper
    {
        public static void Configure(WebApplicationBuilder builder)
        {
            // Retrieve MongoDB settings from configuration.
            var connectionString = builder.Configuration["MongoDB:ConnectionString"];
            var databaseName = builder.Configuration["MongoDB:DatabaseName"];

            // 3. Register MongoDB.Driver's IMongoClient for your other services.
            var mongoClient = new MongoClient(connectionString);
            builder.Services.AddSingleton<IMongoClient>(mongoClient);

            builder.Services.AddIdentityMongoDbProvider<ApplicationUser, ApplicationRole, Guid>(identity =>
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
        }
    }
}
