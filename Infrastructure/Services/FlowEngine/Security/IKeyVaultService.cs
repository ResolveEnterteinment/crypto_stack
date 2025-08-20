namespace Infrastructure.Services.FlowEngine.Security
{
    public interface IKeyVaultService
    {
        Task<SigningKeyInfo> GetCurrentSigningKeyAsync(CancellationToken cancellationToken);
        Task<SigningKeyInfo?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken);
        Task RotateSigningKeyAsync(CancellationToken cancellationToken);
    }
}
