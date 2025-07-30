using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Constants for KYC system
namespace Domain.Constants.KYC
{
    public static class VerificationCheckType
    {
        public const string Document = "DOCUMENT";
        public const string Biometric = "BIOMETRIC";
        public const string PersonalData = "PERSONAL_DATA";
        public const string Address = "ADDRESS";
        public const string Aml = "AML";
        public const string Pep = "PEP";
        public const string Sanctions = "SANCTIONS";
        public const string DeviceTrust = "DEVICE_TRUST";
    }
}
