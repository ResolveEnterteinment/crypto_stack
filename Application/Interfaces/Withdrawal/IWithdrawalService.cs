// Application/Interfaces/Withdrawal/IWithdrawalService.cs
using Application.Contracts.Requests.Withdrawal;
using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.DTOs.Network;
using Domain.DTOs.Withdrawal;
using Domain.Events;
using Domain.Models.Subscription;
using Domain.Models.Withdrawal;
using MediatR;

namespace Application.Interfaces.Withdrawal
{
    public interface IWithdrawalService
    {
        Task<ResultWrapper<WithdrawalLimitDto>> GetUserWithdrawalLimitsAsync(Guid userId);
        Task<ResultWrapper<WithdrawalRequestDto>> RequestWithdrawalAsync(WithdrawalRequest request);
        Task<ResultWrapper<List<WithdrawalData>>> GetUserWithdrawalHistoryAsync(Guid userId);
        Task<ResultWrapper<WithdrawalData>> GetWithdrawalDetailsAsync(Guid withdrawalId);
        Task<ResultWrapper> UpdateWithdrawalStatusAsync(Guid withdrawalId, string status, Guid processedBy, string? comment = null, string? transationHash = null);
        Task<ResultWrapper<PaginatedResult<WithdrawalData>>> GetPendingWithdrawalsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper<decimal>> GetUserWithdrawalTotalsAsync(Guid userId, DateTime startDate, DateTime endDate);
        Task<ResultWrapper<decimal>> GetMinimumWithdrawalThresholdAsync(string assetTicker);
        Task<ResultWrapper<bool>> CanUserWithdrawAsync(Guid userId, decimal amount, string ticker);
        Task<ResultWrapper<List<NetworkDto>>> GetSupportedNetworksAsync(string assetTicker);
        Task<ResultWrapper<bool>> ValidateWithdrawalAddressAsync(string network, string address);
        Task<ResultWrapper<decimal>> GetUserPendingTotalsAsync(Guid userId, string assetTicker);
    }
}