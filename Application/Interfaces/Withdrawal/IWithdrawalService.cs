// Application/Interfaces/Withdrawal/IWithdrawalService.cs
using Domain.DTOs;
using Domain.DTOs.Withdrawal;
using Domain.Models.Withdrawal;

namespace Application.Interfaces.Withdrawal
{
    public interface IWithdrawalService
    {
        Task<ResultWrapper<WithdrawalLimitDto>> GetUserWithdrawalLimitsAsync(Guid userId);
        Task<ResultWrapper<WithdrawalRequestDto>> RequestWithdrawalAsync(WithdrawalRequest request);
        Task<ResultWrapper<List<WithdrawalData>>> GetUserWithdrawalHistoryAsync(Guid userId);
        Task<ResultWrapper<WithdrawalData>> GetWithdrawalDetailsAsync(Guid withdrawalId);
        Task<ResultWrapper> UpdateWithdrawalStatusAsync(Guid withdrawalId, string status, string comment = null);
        Task<ResultWrapper<PaginatedResult<WithdrawalData>>> GetPendingWithdrawalsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper<decimal>> GetUserWithdrawalTotalsAsync(Guid userId, DateTime startDate, DateTime endDate);
        Task<ResultWrapper<bool>> CanUserWithdrawAsync(Guid userId, decimal amount);
    }
}