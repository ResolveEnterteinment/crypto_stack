using Domain.DTOs.Asset;

namespace Application.Contracts.Responses.Withdrawal
{
    public class CanUserWithdrawResponse : BaseResponse
    {
        public required bool CanWithdraw { get; set; } = false;
    }
}
