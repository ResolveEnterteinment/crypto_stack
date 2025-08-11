using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    public class CreateSessionRequest
    {
        //[Required]
        //[RegularExpression("^(BASIC|STANDARD|ADVANCED|ENHANCED)$", ErrorMessage = "Invalid verification level")]
        //public string VerificationLevel { get; set; } = "STANDARD";
    }
}
