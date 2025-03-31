using Encryption.Services;
using Microsoft.Extensions.DependencyInjection;
using IEncryptionService = Application.Interfaces.IEncryptionService;

namespace Encryption
{
    /// <summary>
    /// Extension methods for configuring encryption services
    /// </summary>
    public static class EncryptionServiceExtensions
    {
        /// <summary>
        /// Adds encryption services to the service collection
        /// </summary>
        public static IServiceCollection AddEncryptionServices(this IServiceCollection services)
        {
            services.AddSingleton<IEncryptionService, EncryptionService>();
            return services;
        }

        /// <summary>
        /// Generates and outputs new encryption keys
        /// </summary>
        /// <returns>A tuple with the Base64-encoded Key and IV</returns>
        public static (string Key, string IV) GenerateEncryptionKeys()
        {
            return EncryptionService.GenerateKeys();
        }
    }
}