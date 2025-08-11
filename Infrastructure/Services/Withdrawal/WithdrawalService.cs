// Infrastructure/Services/WithdrawalService.cs
using Application.Contracts.Requests.Withdrawal;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.KYC;
using Application.Interfaces.Network;
using Application.Interfaces.Withdrawal;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.Constants.Logging;
using Domain.Constants.Withdrawal;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.DTOs.Network;
using Domain.DTOs.Settings;
using Domain.DTOs.Withdrawal;
using Domain.Events;
using Domain.Exceptions;
using Domain.Exceptions.KYC;
using Domain.Exceptions.Withdrawal;
using Domain.Models.Balance;
using Domain.Models.Transaction;
using Domain.Models.Withdrawal;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services.Withdrawal
{
    public class WithdrawalService : BaseService<WithdrawalData>, IWithdrawalService
    {
        private readonly IOptions<WithdrawalServiceSettings> _settings;
        private readonly IKycService _kycService;
        private readonly IUserService _userService;
        private readonly IExchangeService _exchangeService;
        private readonly INetworkService _networkService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly ITransactionService _transactionService;

        public WithdrawalService(
            IServiceProvider serviceProvider,
            IOptions<WithdrawalServiceSettings> settings,
            IKycService kycService,
            IUserService userService,
            IExchangeService exchangeService,
            INetworkService networkService,
            IAssetService assetService,
            IBalanceService balanceService,
            ITransactionService transactionService,
            INotificationService notificationService
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                    new CreateIndexModel<WithdrawalData>(
                        Builders<WithdrawalData>.IndexKeys.Ascending(w => w.UserId),
                        new CreateIndexOptions { Name = "UserId_1" }),
                    new CreateIndexModel<WithdrawalData>(
                        Builders<WithdrawalData>.IndexKeys.Ascending(w => w.Status),
                        new CreateIndexOptions { Name = "Status_1" }),
                    new CreateIndexModel<WithdrawalData>(
                        Builders<WithdrawalData>.IndexKeys.Ascending(w => w.CreatedAt),
                        new CreateIndexOptions { Name = "CreatedAt_1" })
                    ]
            })
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _kycService = kycService ?? throw new ArgumentNullException(nameof(kycService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _exchangeService = exchangeService ?? throw new ArgumentException(nameof(exchangeService));
            _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _balanceService = balanceService ?? throw new ArgumentNullException(nameof(balanceService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(balanceService));
        }

        public async Task<ResultWrapper<WithdrawalLimitDto>> GetUserWithdrawalLimitsAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetUserWithdrawalLimitsAsync(Guid userId)",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Get user's KYC status
                    var kycResult = await _kycService.GetUserKycStatusAsync(userId);
                    if (!kycResult.IsSuccess)
                        if (kycResult == null || !kycResult.IsSuccess)
                        {
                            throw new KycVerificationException(kycResult?.ErrorMessage ?? "Kyc fetch result returned null");
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

                        return limitsDto;
                    }

                    // Calculate today's total withdrawals
                    var todayStart = DateTime.UtcNow.Date;
                    var todayEnd = todayStart.AddDays(1);

                    var todayTotal = await GetUserWithdrawalTotalsAsync(userId, todayStart, todayEnd);
                    if (!todayTotal.IsSuccess)
                    {
                        throw new WithdrawalLimitException(todayTotal.ErrorMessage);
                    }

                    limitsDto.DailyUsed = todayTotal.Data;
                    limitsDto.DailyRemaining = Math.Max(0, limitsDto.DailyLimit - limitsDto.DailyUsed);

                    // Calculate this month's total withdrawals
                    var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                    var nextMonth = currentMonth.AddMonths(1);

                    var monthlyTotal = await GetUserWithdrawalTotalsAsync(userId, currentMonth, nextMonth);
                    if (!monthlyTotal.IsSuccess)
                    {
                        throw new WithdrawalLimitException(monthlyTotal.ErrorMessage);
                    }

                    limitsDto.MonthlyUsed = monthlyTotal.Data;
                    limitsDto.MonthlyRemaining = Math.Max(0, limitsDto.MonthlyLimit - limitsDto.MonthlyUsed);

                    // Set the reset date to the first day of next month
                    limitsDto.PeriodResetDate = nextMonth;

                    return limitsDto;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<NetworkDto>>> GetSupportedNetworksAsync(string assetTicker)
        {
            if (string.IsNullOrWhiteSpace(assetTicker))
                ResultWrapper<List<NetworkDto>>.Failure(FailureReason.ValidationError, "Asset ticker is required");
            
            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Withdrawal",
                   FileName = "WithdrawalService",
                   OperationName = "GetSupportedNetworksAsync(string assetTicker)",
                   State = {
                        ["Ticker"] = assetTicker,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
               {
                   var result = await _networkService.GetNetworksByAssetAsync(assetTicker);

                   if (result == null || !result.IsSuccess)
                       throw new DatabaseException($"Failed to fetch networks by asset: {result?.ErrorCode ?? "Fetch result returned null"}");

                   return result.Data;
               })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<WithdrawalReceiptDto>> RequestCryptoWithdrawalAsync(CryptoWithdrawalRequest request)
        {
            // Validate the request
            var validationResult = await ValidateCryptoWithdrawalRequestAsync(request);
            if (!validationResult.IsSuccess)
            {
                return ResultWrapper<WithdrawalReceiptDto>.ValidationError(
                    validationResult.ValidationErrors,
                    validationResult.ErrorMessage);
            }

            return await _resilienceService.CreateBuilder(
               new Scope
               {
                   NameSpace = "Infrastructure.Services.Withdrawal",
                   FileName = "WithdrawalService",
                   OperationName = "RequestCryptoWithdrawalAsync(CryptoWithdrawalRequest request)",
                   State = {
                       ["UserId"] = request.UserId,
                       ["WithdrawalMethod"] = request.WithdrawalMethod,
                       ["Amount"] = request.Amount,
                       ["Currency"] = request.Currency,
                       ["Network"] = request.Network,
                       ["WithdrawalAddress"] = request.WithdrawalAddress,
                       ["Memo"] = request.Memo,
                   },
                   LogLevel = LogLevel.Error
               },
               async () =>
               {
                   // Check if user can withdraw this amount
                   var canWithdrawResult = await CanUserWithdrawAsync(request.UserId, request.Amount, request.Currency);

                   if (canWithdrawResult == null || !canWithdrawResult.IsSuccess)
                   {
                       throw new WithdrawalLimitException(canWithdrawResult?.ErrorMessage ?? "Eligibility check returned null");
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

                       throw new WithdrawalLimitException(errorMessage);
                   }

                   // Get user KYC level
                   var kycResult = await _kycService.GetUserKycStatusAsync(request.UserId);

                   if (kycResult == null || !kycResult.IsSuccess)
                   {
                       throw new KycVerificationException(kycResult?.ErrorMessage ?? "KYC fetch result returned null");
                   }

                   var kycData = kycResult.Data;

                   // Check if user is eligible (approved KYC status)
                   if (kycData.Status != KycStatus.Approved)
                   {
                       throw new KycVerificationException("KYC verification must be approved before making withdrawals.");
                   }

                   // Get user info
                   var userResult = await _userService.GetAsync(request.UserId);

                   var exchangeRateResult = await _exchangeService.GetCachedAssetPriceAsync(request.Currency);
                   decimal exchangeRate = exchangeRateResult?.Data ?? 0;

                   // Create withdrawal record
                   var withdrawal = new WithdrawalData
                   {
                       Id = Guid.NewGuid(),
                       UserId = request.UserId,
                       RequestedBy = userResult?.Email ?? request.UserId.ToString(),
                       Amount = request.Amount,
                       Value = request.Amount * exchangeRate,
                       Currency = request.Currency,
                       WithdrawalMethod = request.WithdrawalMethod,
                       Status = WithdrawalStatus.Pending,
                       CreatedAt = DateTime.UtcNow,
                       KycLevelAtTime = kycData.VerificationLevel,
                       AdditionalDetails = new()
                       {
                           ["WithdrawalAddress"] = request.WithdrawalAddress,
                           ["Network"] = request.Network,
                           ["Memo"] = request.Memo,
                       }
                   };

                   // Save to database
                   var insertResult = await InsertAsync(withdrawal);
                   if (!insertResult.IsSuccess)
                   {
                       throw new DatabaseException($"Failed to create withdrawal request record: {insertResult?.ErrorMessage ?? "Insert result returned null"}");
                   }

                   // Return the withdrawal dto
                   var dto = new WithdrawalReceiptDto
                   {
                       Id = withdrawal.Id,
                       Amount = withdrawal.Amount,
                       Currency = withdrawal.Currency,
                       Status = withdrawal.Status,
                       CreatedAt = withdrawal.CreatedAt,
                       WithdrawalMethod = withdrawal.WithdrawalMethod,
                       WithdrawalAddress = request.WithdrawalAddress
                   };

                   return dto;
               })
                .OnSuccess(async result =>
                {
                    // Notify user
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = request.UserId.ToString(),
                        Message = $"Your withdrawal request for {request.Amount} {request.Currency} has been received and is pending approval."
                    });
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<WithdrawalData>>> GetUserWithdrawalHistoryAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetUserWithdrawalHistoryAsync(Guid userId))",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<WithdrawalData>.Filter.Eq(w => w.UserId, userId);
                    var sort = Builders<WithdrawalData>.Sort.Descending(w => w.CreatedAt);

                    var result = await GetManyAsync(filter);

                    if (result== null || !result.IsSuccess)
                        throw new DatabaseException("Failed to fetch user withdrawals");

                    return result.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<WithdrawalData>> GetWithdrawalDetailsAsync(Guid withdrawalId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetWithdrawalDetailsAsync(Guid withdrawalId)",
                    State = {
                        ["WithdrawalId"] = withdrawalId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var result = await GetByIdAsync(withdrawalId);

                    if (result == null || !result.IsSuccess || result.Data == null)
                    {
                        throw new DatabaseException($"Failed to fetch withdrawal details: {result?.ErrorMessage ?? "Fetch result returned null"}");
                    }
                    
                    return result.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<WithdrawalData>>> UpdateWithdrawalStatusAsync(Guid withdrawalId, string status, Guid processedBy, string? comment = null, string? transactionHash = null)
        {
            if (string.IsNullOrWhiteSpace(status) || !WithdrawalStatus.AllValues.Contains(status.ToUpperInvariant()))
            {
                return ResultWrapper<CrudResult<WithdrawalData>>.Failure(
                    FailureReason.ValidationError,
                    $"Invalid status. Must be one of: {string.Join(", ", WithdrawalStatus.AllValues)}");
            }

            var result = await GetByIdAsync(withdrawalId);
            if (result == null || !result.IsSuccess || result.Data == null)
            {
                return ResultWrapper<CrudResult<WithdrawalData>>.FromException(
                    new ResourceNotFoundException("Withrawal", withdrawalId.ToString()));
            }

            var withdrawal = result.Data;

            // Don't allow updating already completed withdrawals
            if (withdrawal.Status is WithdrawalStatus.Completed or
                WithdrawalStatus.Cancelled)
            {
                return ResultWrapper<CrudResult<WithdrawalData>>.Failure(
                    FailureReason.ValidationError,
                    $"Cannot update status of a {withdrawal.Status.ToLower()} withdrawal.");
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "UpdateWithdrawalStatusAsync(Guid withdrawalId, string status, Guid processedBy, string? comment = null, string? transactionHash = null)",
                    State = {
                        ["WithdrawalId"] = withdrawalId,
                        ["Status"] = status,
                        ["ProcessedBy"] = processedBy,
                        ["Comment"] = comment,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
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
                    if (updateResult == null || !updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update withdrawal status: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                    }

                    return updateResult.Data;
                })
                .OnSuccess(async result =>
                {
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

                    if (status == WithdrawalStatus.Approved)
                        await HandleWithdrawalApproved(withdrawal);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaginatedResult<WithdrawalData>>> GetPendingWithdrawalsAsync(int page = 1, int pageSize = 20)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetPendingWithdrawalsAsync(int page = 1, int pageSize = 20)",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<WithdrawalData>.Filter.Eq(w => w.Status, WithdrawalStatus.Pending);
                    var sort = Builders<WithdrawalData>.Sort.Ascending(w => w.CreatedAt);

                    var result = await GetPaginatedAsync(filter, page, pageSize, "CreatedAt", true);

                    if (result == null || !result.IsSuccess)
                        throw new DatabaseException($"Failed to fetch pending withdrawals: {result?.ErrorMessage ?? "Fetch result returned nulls"} ");

                    return result.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<decimal>> GetUserWithdrawalTotalsAsync(Guid userId, DateTime startDate, DateTime endDate)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetUserWithdrawalTotalsAsync(Guid userId, DateTime startDate, DateTime endDate)",
                    State =
                    {
                        ["UserId"] = userId,
                        ["StartDate"] = startDate,
                        ["EndDate"] = endDate,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
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
                    if (result == null || !result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to get withdrawal totals: {result?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var totalValues = result.Data?.Sum(w => w.Value) ?? 0m;
                    return totalValues;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<decimal>> GetUserPendingTotalsAsync(Guid userId, string assetTicker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetUserPendingTotalsAsync(Guid userId, string assetTicker)",
                    State =
                        {
                            ["UserId"] = userId,
                            ["Ticker"] = assetTicker,
                        },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<WithdrawalData>.Filter.And(
                    Builders<WithdrawalData>.Filter.Eq(w => w.UserId, userId),
                    Builders<WithdrawalData>.Filter.Eq(w => w.Currency, assetTicker),
                    Builders<WithdrawalData>.Filter.Eq(w => w.Status, WithdrawalStatus.Pending)
                );

                    var result = await GetManyAsync(filter);
                    if (result is null || !result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to get withdrawal totals: {result?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var total = result?.Data.Sum(w => w.Amount) ?? 0m;
                    return total;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<decimal>> GetMinimumWithdrawalThresholdAsync (string assetTicker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "GetUserPendingTotalsAsync(Guid userId, string assetTicker)",
                    State =
                        {
                            ["Ticker"] = assetTicker,
                        },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var rate = await _exchangeService.GetCachedAssetPriceAsync(assetTicker);

                    if (rate is null || !rate.IsSuccess)
                    {
                        throw new ExchangeApiException(rate?.ErrorMessage ?? "Rate fetch result returned null");
                    }
                    var minimumWithdrawalValue = _settings.Value.MinimumWithdrawalValue;
                    var minimumWithdrawalThreshold = Math.Round(minimumWithdrawalValue / rate.Data, 18, MidpointRounding.AwayFromZero);

                    return minimumWithdrawalThreshold;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> CanUserWithdrawAsync(Guid userId, decimal amount, string ticker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Withdrawal",
                    FileName = "WithdrawalService",
                    OperationName = "CanUserWithdrawAsync(Guid userId, decimal amount, string ticker)",
                    State =
                        {
                            ["UserId"] = userId,
                            ["Amount"] = amount,
                            ["Ticker"] = ticker,
                        },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Get the user's withdrawal limits
                    var limitsResult = await GetUserWithdrawalLimitsAsync(userId);
                    if (!limitsResult.IsSuccess)
                    {
                        throw new WithdrawalLimitException($"Failed to fetch user withdrawal limits: {limitsResult?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var limits = limitsResult.Data;

                    // Get withdrawal asset rate
                    var rateResult = await _exchangeService.GetCachedAssetPriceAsync(ticker);

                    if (rateResult is null || !rateResult.IsSuccess)
                    {
                        throw new ExchangeApiException(rateResult?.ErrorMessage ?? "Rate fetch result returned null");
                    }

                    decimal rate = rateResult.Data;
                    decimal withdrawalValue = amount * rate;

                    // Check if the amount is below minimum threshold
                    if (limits.DailyRemaining < _settings.Value.MinimumWithdrawalValue)
                    {
                        throw new WithdrawalLimitException($"Remaining withdrawal limit is less than minimum threshold.");
                    }
                    else if (withdrawalValue < _settings.Value.MinimumWithdrawalValue)
                    {
                        var minimumAmount = Math.Round(_settings.Value.MinimumWithdrawalValue / rate, 8, MidpointRounding.AwayFromZero);
                        throw new WithdrawalLimitException($"Minimum withdrawal value is {_settings.Value.MinimumWithdrawalValue:N2} {_settings.Value.MinimumWithdrawalTicker} or {minimumAmount:F8} {ticker}");
                    }

                    // Check if the amount exceeds any limits

                    else if (withdrawalValue > limits.DailyRemaining)
                    {
                        var maximumAmount = Math.Round(limits.DailyRemaining / rate, 8, MidpointRounding.ToZero);
                        throw new WithdrawalLimitException($"Daily withdrawal limit exceeded. Maximum withdrawal amount is {limits.DailyRemaining:N2} {_settings.Value.MinimumWithdrawalTicker} or {maximumAmount:F8} {ticker}");
                    }

                    else if (withdrawalValue > limits.MonthlyRemaining)
                    {
                        var maximumAmount = Math.Round(limits.MonthlyRemaining / rate, 8, MidpointRounding.ToZero);
                        throw new WithdrawalLimitException($"Monthly withdrawal limit exceeded. Maximum withdrawal amount is {limits.MonthlyRemaining:N2} {_settings.Value.MinimumWithdrawalTicker} or {maximumAmount:F8} {ticker}");
                    }


                    return true;
                })
                .ExecuteAsync();
        }

        private async Task<ResultWrapper<bool>> ValidateCryptoWithdrawalRequestAsync(CryptoWithdrawalRequest request)
        {
            var validationErrors = new Dictionary<string, string[]>();

            if (request.UserId == Guid.Empty)
            {
                validationErrors.Add("UserId", new[] { "Invalid user ID." });
            }

            if (request.WithdrawalMethod != WithdrawalMethod.CryptoTransfer)
            {
                validationErrors.Add("WithdrawalMethod", new[] { "Invalid withdrawal method." });
            }

            if (request.Amount <= 0)
            {
                validationErrors.Add("Amount", new[] { "Withdrawal amount must be greater than zero." });
            }


            // If we have validation errors so far, return them without making network calls
            if (validationErrors.Any())
            {
                return ResultWrapper<bool>.ValidationError(validationErrors, "Validation failed for crypto withdrawal request.");
            }

            // Validate ticker
            var supportedTickers = await _assetService.GetSupportedTickersAsync();

            if (string.IsNullOrWhiteSpace(request.Currency) || 
                supportedTickers == null ||
                !supportedTickers.IsSuccess ||
                !supportedTickers.Data.Contains(request.Currency))
            {
                validationErrors.Add("Currency", new[] { "Currency is not provided or invalid." });
            }

            // Validate network
            var supportedNetworksResult = await _networkService.GetNetworkByNameAsync(request.Network);

            if (string.IsNullOrWhiteSpace(request.Network) || 
                supportedNetworksResult == null || 
                !supportedNetworksResult.IsSuccess)
            {
                validationErrors.Add("Network", new[] { $"Network is not provided or invalid." });
            }

            // Validate network
            var networkResult = await _networkService.GetNetworkByNameAsync(request.Network);
            if (networkResult == null || !networkResult.IsSuccess || networkResult.Data == null)
            {
                validationErrors.Add("Network", new[] { "Invalid network." });
                return ResultWrapper<bool>.ValidationError(validationErrors, "Invalid network specified.");
            }

            // Validate address
            var isAddressValid = await 
                ValidateCryptoWithdrawalAddressAsync(request.Network, request.WithdrawalAddress);

            if (!isAddressValid)
            {
                validationErrors.Add("WithdrawalAddress", new[] { "Invalid withdrawal address." });
            }

            // Validate memo if required
            var requiresMemo = networkResult.Data.RequiresMemo;
            if (requiresMemo && string.IsNullOrWhiteSpace(request.Memo))
            {
                validationErrors.Add("Memo", new[] { "Memo is required for this network." });
                return ResultWrapper<bool>.ValidationError(validationErrors, "Memo is required for the specified network.");
            }

            // All validations passed
            return ResultWrapper<bool>.Success(true, "Validation successful.");
        }

        // Add this helper method for address validation
        private async Task<bool> ValidateCryptoWithdrawalAddressAsync(string network, string address)
        {
            var validateResult = await _networkService.IsCryptoAddressValidAsync(network, address);
            if (validateResult == null || !validateResult.IsSuccess)
            {
                return false;
            }
            return validateResult.Data;
        }
        private async Task HandleWithdrawalApproved(WithdrawalData withdrawal)
        {
            await _eventService!.PublishAsync(new WithdrawalApprovedEvent(withdrawal));
        }
    }
}