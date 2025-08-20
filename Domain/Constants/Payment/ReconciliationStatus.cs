using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Constants.Payment
{
    public static class ReconciliationStatus
    {
        public const string Pending = "PENDING";
        public const string Partial = "PARTIAL";
        public const string Complete = "COMPLETE";
        public const string Failed = "FAILED";
    }
}
