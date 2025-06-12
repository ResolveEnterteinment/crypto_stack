// Infrastructure/Services/WithdrawalService.cs
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Application.Interfaces.Network;
using Application.Interfaces.Withdrawal;
using Domain.Constants.KYC;
using Domain.Constants.Withdrawal;
using Domain.DTOs;
using Domain.DTOs.Network;
using Domain.DTOs.Withdrawal;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using Domain.Models.Withdrawal;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services.Withdrawal
{
    public class WithdrawalService : BaseService<WithdrawalData>, IWithdrawalService
    {
        private readonly IKycService _kycService;
        private readonly IUserService _userService;
        private readonly INotificationService _notificationService;
        private readonly INetworkService _networkService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly ITransactionService _transactionService;

        public WithdrawalService(
            ICrudRepository<WithdrawalData> repository,
            ICacheService<WithdrawalData> cacheService,
            IMongoIndexService<WithdrawalData> indexService,
            ILoggingService logger,
            IEventService eventService,
            IKycService kycService,
            IUserService userService,
            INetworkService networkService,
            IAssetService assetService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            INotificationService notificationService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<WithdrawalData>(
                    Builders<WithdrawalData>.IndexKeys.Ascending(w => w.UserId),
                    new CreateIndexOptions { Name = "UserId_1" }
                ),
                new CreateIndexModel<WithdrawalData>(
                    Builders<WithdrawalData>.IndexKeys.Ascending(w => w.Status),
                    new CreateIndexOptions { Name = "Status_1" }
                ),
                new CreateIndexModel<WithdrawalData>(
                    Builders<WithdrawalData>.IndexKeys.Ascending(w => w.CreatedAt),
                    new CreateIndexOptions { Name = "CreatedAt_1" }
                )
            }
        )
        {
            _kycService = kycService ?? throw new ArgumentNullException(nameof(kycService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(balanceService));
        }

        public async Task<ResultWrapper<WithdrawalLimitDto>> GetUserWithdrawalLimitsAsync(Guid userId)
        {
            try
            {
                // Get user's KYC status
                var kycResult = await _kycService.GetUserKycStatusAsync(userId, KycStatus.Approved);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<WithdrawalLimitDto>.Failure(
                        kycResult.Reason,
                        kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Set limits based on KYC level
                var limitsDto = new WithdrawalLimitDto
                {
                    KycLevel = kycData.VerificationLevel.ToUpperInvariant()
                };

                // Set daily and monthly limits based on KYC level
                switch (kycData.VerificationLevel.ToUpperInvariant())
                {
                    case KycLevel.None:
                        limitsDto.DailyLimit = WithdrawalLimits.NONE_DAILY_LIMIT;
                        limitsDto.MonthlyLimit = WithdrawalLimits.NONE_MONTHLY_LIMIT;
                        break;
                    case KycLevel.Basic:
                        limitsDto.DailyLimit = WithdrawalLimits.BASIC_DAILY_LIMIT;
                        limitsDto.MonthlyLimit = WithdrawalLimits.BASIC_MONTHLY_LIMIT;
                        break;
                    case KycLevel.Standard:
                        limitsDto.DailyLimit = WithdrawalLimits.STANDARD_DAILY_LIMIT;
                        limitsDto.MonthlyLimit = WithdrawalLimits.STANDARD_MONTHLY_LIMIT;
                        break;
                    case KycLevel.Advanced:
                        limitsDto.DailyLimit = WithdrawalLimits.ADVANCED_DAILY_LIMIT;
                        limitsDto.MonthlyLimit = WithdrawalLimits.ADVANCED_MONTHLY_LIMIT;
                        break;
                    case KycLevel.Enhanced:
                        limitsDto.DailyLimit = WithdrawalLimits.ENHANCED_DAILY_LIMIT;
                        limitsDto.MonthlyLimit = WithdrawalLimits.ENHANCED_MONTHLY_LIMIT;
                        break;
                    default:
                        limitsDto.DailyLimit = 0;
                        limitsDto.MonthlyLimit = 0;
                        break;
                }

                // If user is not verified or KYC rejected, return zeros for limits
                if (kycData.Status != KycStatus.Approved)
                {
                    limitsDto.DailyLimit = 0;
                    limitsDto.MonthlyLimit = 0;
                    limitsDto.DailyRemaining = 0;
                    limitsDto.MonthlyRemaining = 0;
                    limitsDto.DailyUsed = 0;
                    limitsDto.MonthlyUsed = 0;
                    limitsDto.PeriodResetDate = DateTime.UtcNow.AddDays(1);

                    return ResultWrapper<WithdrawalLimitDto>.Success(limitsDto);
                }

                // Calculate today's total withdrawals
                var todayStart = DateTime.UtcNow.Date;
                var todayEnd = todayStart.AddDays(1);

                var todayTotal = await GetUserWithdrawalTotalsAsync(userId, todayStart, todayEnd);
                if (!todayTotal.IsSuccess)
                {
                    return ResultWrapper<WithdrawalLimitDto>.Failure(
                        todayTotal.Reason,
                        todayTotal.ErrorMessage);
                }

                limitsDto.DailyUsed = todayTotal.Data;
                limitsDto.DailyRemaining = Math.Max(0, limitsDto.DailyLimit - limitsDto.DailyUsed);

                // Calculate this month's total withdrawals
                var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                var nextMonth = currentMonth.AddMonths(1);

                var monthlyTotal = await GetUserWithdrawalTotalsAsync(userId, currentMonth, nextMonth);
                if (!monthlyTotal.IsSuccess)
                {
                    return ResultWrapper<WithdrawalLimitDto>.Failure(
                        monthlyTotal.Reason,
                        monthlyTotal.ErrorMessage);
                }

                limitsDto.MonthlyUsed = monthlyTotal.Data;
                limitsDto.MonthlyRemaining = Math.Max(0, limitsDto.MonthlyLimit - limitsDto.MonthlyUsed);

                // Set the reset date to the first day of next month
                limitsDto.PeriodResetDate = nextMonth;

                return ResultWrapper<WithdrawalLimitDto>.Success(limitsDto);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting withdrawal limits: {ex.Message}");
                return ResultWrapper<WithdrawalLimitDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<List<NetworkDto>>> GetSupportedNetworksAsync(string assetTicker)
        {
            try
            {
                return string.IsNullOrWhiteSpace(assetTicker)
                    ? ResultWrapper<List<NetworkDto>>.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        "Asset ticker is required")
                    : await _networkService.GetNetworksByAssetAsync(assetTicker);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting supported networks: {ex.Message}");
                return ResultWrapper<List<NetworkDto>>.FromException(ex);
            }
        }

        // Add this helper method for address validation
        public async Task<ResultWrapper<bool>> ValidateWithdrawalAddressAsync(string network, string address)
        {
            return await _networkService.IsAddressValidAsync(network, address);
        }

        public async Task<ResultWrapper<WithdrawalRequestDto>> RequestWithdrawalAsync(WithdrawalRequest request)
        {
            try
            {
                // Validate the request
                if (request.Amount <= 0)
                {
                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        "Withdrawal amount must be greater than zero.");
                }

                // Check if user can withdraw this amount
                var canWithdrawResult = await CanUserWithdrawAsync(request.UserId, request.Amount);
                if (!canWithdrawResult.IsSuccess)
                {
                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        canWithdrawResult.Reason,
                        canWithdrawResult.ErrorMessage);
                }

                if (!canWithdrawResult.Data)
                {
                    // Get the limits to provide informative error message
                    var limitsResult = await GetUserWithdrawalLimitsAsync(request.UserId);

                    string errorMessage = "Withdrawal limit exceeded.";

                    if (limitsResult.IsSuccess)
                    {
                        var limits = limitsResult.Data;
                        errorMessage = $"Withdrawal exceeds your limits. Daily remaining: {limits.DailyRemaining} {request.Currency}, " +
                                     $"Monthly remaining: {limits.MonthlyRemaining} {request.Currency}";
                    }

                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        errorMessage);
                }

                // Get user KYC level
                var kycResult = await _kycService.GetUserKycStatusAsync(request.UserId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        kycResult.Reason,
                        kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Check if user is eligible (approved KYC status)
                if (kycData.Status != KycStatus.Approved)
                {
                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        "KYC verification must be approved before making withdrawals.");
                }

                // Get user info
                var userResult = await _userService.GetAsync(request.UserId);

                // Create withdrawal record
                var withdrawal = new WithdrawalData
                {
                    Id = Guid.NewGuid(),
                    UserId = request.UserId,
                    RequestedBy = userResult?.Email ?? request.UserId.ToString(),
                    Amount = request.Amount,
                    Currency = request.Currency,
                    WithdrawalMethod = request.WithdrawalMethod,
                    WithdrawalAddress = request.WithdrawalAddress,
                    Status = WithdrawalStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    KycLevelAtTime = kycData.VerificationLevel,
                    AdditionalDetails = request.AdditionalDetails
                };

                // Save to database
                var insertResult = await InsertAsync(withdrawal);
                if (!insertResult.IsSuccess)
                {
                    return ResultWrapper<WithdrawalRequestDto>.Failure(
                        insertResult.Reason,
                        $"Failed to create withdrawal request: {insertResult.ErrorMessage}");
                }

                // Notify user
                _ = await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = request.UserId.ToString(),
                    Message = $"Your withdrawal request for {request.Amount} {request.Currency} has been received and is pending approval."
                });

                // Return the withdrawal dto
                var dto = new WithdrawalRequestDto
                {
                    Id = withdrawal.Id,
                    Amount = withdrawal.Amount,
                    Currency = withdrawal.Currency,
                    Status = withdrawal.Status,
                    CreatedAt = withdrawal.CreatedAt,
                    WithdrawalMethod = withdrawal.WithdrawalMethod,
                    WithdrawalAddress = withdrawal.WithdrawalAddress
                };

                return ResultWrapper<WithdrawalRequestDto>.Success(dto);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error creating withdrawal request: {ex.Message}");
                return ResultWrapper<WithdrawalRequestDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<List<WithdrawalData>>> GetUserWithdrawalHistoryAsync(Guid userId)
        {
            return await SafeExecute<List<WithdrawalData>>(
                async () =>
                {
                    var filter = Builders<WithdrawalData>.Filter.Eq(w => w.UserId, userId);
                    var sort = Builders<WithdrawalData>.Sort.Descending(w => w.CreatedAt);

                    var result = await _repository.GetAllAsync(filter) ??
                        throw new KeyNotFoundException("Failed to fetch user withdrawals");
                    return result;
                }
            );
        }

        public async Task<ResultWrapper<WithdrawalData>> GetWithdrawalDetailsAsync(Guid withdrawalId)
        {
            try
            {
                var result = await GetByIdAsync(withdrawalId);
                return !result.IsSuccess || result.Data == null
                    ? ResultWrapper<WithdrawalData>.Failure(
                        Domain.Constants.FailureReason.ResourceNotFound,
                        $"Withdrawal with ID {withdrawalId} not found.")
                    : ResultWrapper<WithdrawalData>.Success(result.Data);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting withdrawal details: {ex.Message}");
                return ResultWrapper<WithdrawalData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateWithdrawalStatusAsync(Guid withdrawalId, string status, Guid processedBy, string? comment = null, string? transactionHash = null)
        {
            using var scope = Logger.BeginScope(new
            {
                WithdrawalId = withdrawalId,
                Status = status,
                ProcessedBy = processedBy,
                Comment = comment
            });
            try
            {
                if (string.IsNullOrWhiteSpace(status) || !WithdrawalStatus.AllValues.Contains(status.ToUpperInvariant()))
                {
                    return ResultWrapper.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        $"Invalid status. Must be one of: {string.Join(", ", WithdrawalStatus.AllValues)}");
                }

                var result = await GetByIdAsync(withdrawalId);
                if (!result.IsSuccess || result.Data == null)
                {
                    return ResultWrapper.Failure(
                        Domain.Constants.FailureReason.ResourceNotFound,
                        $"Withdrawal with ID {withdrawalId} not found.");
                }

                var withdrawal = result.Data;

                // Don't allow updating already completed withdrawals
                if (withdrawal.Status is WithdrawalStatus.Completed or
                    WithdrawalStatus.Cancelled)
                {
                    return ResultWrapper.Failure(
                        Domain.Constants.FailureReason.ValidationError,
                        $"Cannot update status of a {withdrawal.Status.ToLower()} withdrawal.");
                }

                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = status.ToUpperInvariant()
                };

                if (status is WithdrawalStatus.Approved or
                    WithdrawalStatus.Completed or
                    WithdrawalStatus.Rejected)
                {
                    updateFields["ProcessedAt"] = DateTime.UtcNow;
                    updateFields["ProcessedBy"] = processedBy;
                }

                if (!string.IsNullOrEmpty(comment))
                {
                    updateFields["Comments"] = comment;
                }

                if (!string.IsNullOrEmpty(transactionHash))
                {
                    updateFields["TransactionHash"] = transactionHash;
                }

                // Update audit trail
                var auditTrail = withdrawal.AuditTrail ?? [];
                auditTrail[$"StatusChange_{DateTime.UtcNow.Ticks}"] = new WithdrawalAuditTrail
                {
                    OldStatus = withdrawal.Status,
                    NewStatus = status,
                    Timestamp = DateTime.UtcNow,
                    Comment = comment,
                    ProcessedBy = processedBy
                };
                updateFields["AuditTrail"] = auditTrail;

                var updateResult = await UpdateAsync(withdrawalId, updateFields);
                if (!updateResult.IsSuccess)
                {
                    return ResultWrapper.Failure(
                        updateResult.Reason,
                        $"Failed to update withdrawal status: {updateResult.ErrorMessage}");
                }

                // Notify user of status change
                string message = status switch
                {
                    WithdrawalStatus.Approved => $"Your withdrawal request for {withdrawal.Amount} {withdrawal.Currency} has been approved and is being processed.",
                    WithdrawalStatus.Completed => $"Your withdrawal of {withdrawal.Amount} {withdrawal.Currency} has been completed.",
                    WithdrawalStatus.Rejected => $"Your withdrawal request for {withdrawal.Amount} {withdrawal.Currency} has been rejected. Reason: {comment ?? "Not specified"}",
                    WithdrawalStatus.Cancelled => $"Your withdrawal request for {withdrawal.Amount} {withdrawal.Currency} has been cancelled.",
                    _ => $"Your withdrawal request status has been updated to: {status}"
                };

                _ = await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = withdrawal.UserId.ToString(),
                    Message = message
                });

                switch (status)
                {
                    case WithdrawalStatus.Approved:
                        await HandleWithdrawalApproved(withdrawal);
                        break;

                    default:
                        break;
                }

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating withdrawal status: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        private async Task HandleWithdrawalApproved(WithdrawalData withdrawal)
        {
            using var scope = Logger.BeginScope(new
            {
                WithdrawalId = withdrawal.Id,
            });

            try
            {
                var assetResult = await _assetService.GetByTickerAsync(withdrawal.Currency);
                if (assetResult is null || !assetResult.IsSuccess)
                {
                    throw new AssetFetchException();
                }

                var asset = assetResult.Data;
                // Update user's balance
                var balanceUpdate = new BalanceData
                {
                    UserId = withdrawal.UserId,
                    AssetId = asset.Id,
                    Ticker = asset.Ticker,
                    Available = -withdrawal.Amount,
                    LastUpdated = DateTime.UtcNow
                };

                var updateBalanceResult = await _balanceService.UpsertBalanceAsync(
                    withdrawal.UserId, balanceUpdate);

                if (updateBalanceResult is null || !updateBalanceResult.IsSuccess || updateBalanceResult.Data is null)
                {
                    throw new DatabaseException(
                        $"Failed to update balances: {updateBalanceResult?.ErrorMessage ?? "Unknown error"}");
                }

                var balance = updateBalanceResult.Data;

                _ = Logger.EnrichScope(
                    ("BalanceId", balance.Id)
                    );

                // Record the transaction
                var transaction = new TransactionData
                {
                    UserId = withdrawal.UserId,
                    BalanceId = balance.Id,
                    SourceName = "Platform",
                    SourceId = withdrawal.Id.ToString(),
                    Action = $"Withdrawal",
                    Quantity = withdrawal.Amount
                };

                var insertTransactionResult = await _transactionService.InsertAsync(transaction);
                if (insertTransactionResult == null || !insertTransactionResult.IsSuccess)
                {
                    throw new DatabaseException(
                        $"Failed to create transaction record: {insertTransactionResult?.ErrorMessage ?? "Unknown error"}");
                }
                var TransactionScope = Logger.EnrichScope(
                    ("TransactionId", transaction.Id)
                    );

                await EventService!.PublishAsync(new WithdrawalApprovedEvent(withdrawal)
                );
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync(ex.Message, "HandleWithdrawalApproved", Domain.Constants.Logging.LogLevel.Critical, true);
            }
        }

        public async Task<ResultWrapper<PaginatedResult<WithdrawalData>>> GetPendingWithdrawalsAsync(int page = 1, int pageSize = 20)
        {
            try
            {
                var filter = Builders<WithdrawalData>.Filter.Eq(w => w.Status, WithdrawalStatus.Pending);
                //var sort = Builders<WithdrawalData>.Sort.Ascending(w => w.CreatedAt);

                var result = await GetPaginatedAsync(filter, page, pageSize, "CreatedAt");
                return !result.IsSuccess
                    ? ResultWrapper<PaginatedResult<WithdrawalData>>.Failure(
                        result.Reason,
                        $"Failed to get pending withdrawals: {result.ErrorMessage}")
                    : ResultWrapper<PaginatedResult<WithdrawalData>>.Success(result.Data);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting pending withdrawals: {ex.Message}");
                return ResultWrapper<PaginatedResult<WithdrawalData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<decimal>> GetUserWithdrawalTotalsAsync(Guid userId, DateTime startDate, DateTime endDate)
        {
            try
            {
                var filter = Builders<WithdrawalData>.Filter.And(
                    Builders<WithdrawalData>.Filter.Eq(w => w.UserId, userId),
                    Builders<WithdrawalData>.Filter.Gte(w => w.CreatedAt, startDate),
                    Builders<WithdrawalData>.Filter.Lt(w => w.CreatedAt, endDate),
                    Builders<WithdrawalData>.Filter.In(w => w.Status, new[] {
                        WithdrawalStatus.Pending,
                        WithdrawalStatus.Approved,
                        WithdrawalStatus.Completed
                    })
                );

                var result = await GetManyAsync(filter);
                if (!result.IsSuccess)
                {
                    return ResultWrapper<decimal>.Failure(
                        result.Reason,
                        $"Failed to get withdrawal totals: {result.ErrorMessage}");
                }

                var total = result.Data?.Sum(w => w.Amount) ?? 0m;
                return ResultWrapper<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting withdrawal totals: {ex.Message}");
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<decimal>> GetUserPendingTotalsAsync(Guid userId, string assetTicker)
        {
            try
            {
                var filter = Builders<WithdrawalData>.Filter.And(
                    Builders<WithdrawalData>.Filter.Eq(w => w.UserId, userId),
                    Builders<WithdrawalData>.Filter.Eq(w => w.Currency, assetTicker),
                    Builders<WithdrawalData>.Filter.Eq(w => w.Status, WithdrawalStatus.Pending)
                );

                var result = await _repository.GetAllAsync(filter);
                if (result is null)
                {
                    throw new DatabaseException("Failed to get withdrawal totals");
                }

                var total = result?.Sum(w => w.Amount) ?? 0m;
                return ResultWrapper<decimal>.Success(total);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting withdrawal totals: {ex.Message}");
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> CanUserWithdrawAsync(Guid userId, decimal amount)
        {
            try
            {
                // Get the user's withdrawal limits
                var limitsResult = await GetUserWithdrawalLimitsAsync(userId);
                if (!limitsResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(
                        limitsResult.Reason,
                        limitsResult.ErrorMessage);
                }

                var limits = limitsResult.Data;

                // Check if the amount exceeds any limits
                return amount > limits.DailyRemaining
                    ? ResultWrapper<bool>.Success(false,
                        $"Daily withdrawal limit exceeded. Remaining: {limits.DailyRemaining}")
                    : amount > limits.MonthlyRemaining
                    ? ResultWrapper<bool>.Success(false,
                        $"Monthly withdrawal limit exceeded. Remaining: {limits.MonthlyRemaining}")
                    : ResultWrapper<bool>.Success(true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking withdrawal eligibility: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }
    }
}