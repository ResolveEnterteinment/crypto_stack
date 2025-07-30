using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Constants for KYC system
namespace Domain.Constants.KYC
{
    public static class AmlStatus
    {
        public const string Cleared = "CLEARED";
        public const string ReviewRequired = "REVIEW_REQUIRED";
        public const string Blocked = "BLOCKED";
        public const string Pending = "PENDING";
    }
}
