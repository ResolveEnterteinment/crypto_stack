using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.Flow
{
    public class RecoveryResultDto
    {
        public int RecoveredCount { get; set; }
        public int FailedCount { get; set; }
        public List<Guid> RecoveredFlows { get; set; } = [];
        public List<FailedRecoveryDto> FailedFlows { get; set; } = [];
    }
}
