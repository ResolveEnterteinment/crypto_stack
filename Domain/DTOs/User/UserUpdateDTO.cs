using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.User
{
    public class UserUpdateDTO
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PaymentProviderCustomerId { get; set; }
        public bool IsKycVerified { get; set; } = false;
        public string KycLevel { get; set; } = Domain.Constants.KYC.KycLevel.None;
        public DateTime? KycVerifiedAt { get; set; }
    }
}
