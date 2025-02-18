using AspNetCore.Identity.MongoDbCore.Extensions;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;

namespace crypto_investment_project.Server.Helpers
{
    public static class MongoDbIdentityConfigurationHelper
    {
        public static void Configure(WebApplicationBuilder? builder)
        {
            var mongoDbIdentityConfig = new MongoDbIdentityConfiguration
            {
                MongoDbSettings = new MongoDbSettings
                {
                    ConnectionString = builder.Configuration["MongoDB:ConnectionString"],
                    DatabaseName = builder.Configuration["MongoDB:DatabaseName"]
                },
                IdentityOptionsAction = options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequiredLength = 6;
                    options.SignIn.RequireConfirmedEmail = true;

                    // lockout
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                    options.Lockout.MaxFailedAccessAttempts = 5;

                    options.User.RequireUniqueEmail = true;
                }
            };

            builder.Services.ConfigureMongoDbIdentity<ApplicationUser, ApplicationRole, Guid>(mongoDbIdentityConfig)
                .AddUserManager<UserManager<ApplicationUser>>()
                .AddSignInManager<SignInManager<ApplicationUser>>()
                .AddRoleManager<RoleManager<ApplicationRole>>()
                .AddDefaultTokenProviders();
        }
    }
}
