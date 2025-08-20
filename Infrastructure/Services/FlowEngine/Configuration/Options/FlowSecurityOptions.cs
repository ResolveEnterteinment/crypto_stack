using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Configuration.Options
{
    public class FlowSecurityOptions
    {
        public bool EnableEncryption { get; set; } = true;
        public bool EnableAuditLog { get; set; } = true;
        public bool RequireUserAuthorization { get; set; } = true;
        public TimeSpan MaxExecutionTime { get; set; } = TimeSpan.FromHours(24);
        public int MaxDataSize { get; set; } = 10 * 1024 * 1024; // 10MB
    }
}
