using Domain.Models.Balance;

namespace Application.Contracts.Responses.Balance
{
    public class BalanceResponse : BaseResponse
    {

        public BalanceResponse(IEnumerable<BalanceData> balances)
        {

        }
    }
}
