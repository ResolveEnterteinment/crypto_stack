using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Requests.KYC
{
    public class InvalidateSessionRequest
    {
        public string Reason { get; set; } = "User requested";
    }
}
