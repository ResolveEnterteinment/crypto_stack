using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Security
{
    public sealed record SigningKeyInfo
    {
        public string KeyId { get; init; } = string.Empty;
        public byte[] Key { get; init; } = Array.Empty<byte>();
        public DateTime CreatedAt { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsActive { get; init; }
    }
}
