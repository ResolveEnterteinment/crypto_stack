// crypto_investment_project.Server/Controllers/WithdrawalController.cs
using Application.Contracts.Requests.Withdrawal;
using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Application.Interfaces.Logging;
using Application.Interfaces.Withdrawal;
using Domain.Constants;
using Domain.Constants.Withdrawal;
using Domain.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    [EnableRateLimiting("standard")]
    public class WithdrawalController : ControllerBase
    {
        private readonly IWithdrawalService _withdrawalService;
        private readonly IAssetService _assetService;
        private readonly ILoggingService _logger;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IUserService _userService;

        public WithdrawalController(
            IWithdrawalService withdrawalService,
            IAssetService assetService,
            ILoggingService logger,
            IIdempotencyService idempotencyService,
            IUserService userService)
        {
            _withdrawalService = withdrawalService ?? throw new ArgumentNullException(nameof(withdrawalService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        /// <summary>
        /// Retrieves withdrawal limits for the authenticated user
        /// </summary>
        /// <returns>User's current withdrawal limits</returns>
        /// <response code="200">Returns the user's withdrawal limits</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet("limits/user/current")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWithdrawalLimits()
        {
            using var scope = _logger.BeginScope();

            try
            {
                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // ETag support for caching
                var etagKey = $"withdrawal_limits_user_{userId}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    _logger.LogWarning("Already processed.");
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                var result = await _withdrawalService.GetUserWithdrawalLimitsAsync(userId);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch withdrawal limits for user ID {userId}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\"";
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved withdrawal limits for user {UserId}", userId);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving withdrawal limits: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Retrieves withdrawal limits for a specific user (Admin only)
        /// </summary>
        /// <param name="user">User GUID</param>
        /// <returns>User's withdrawal limits</returns>
        /// <response code="200">Returns the user's withdrawal limits</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet("admin/limits/user/{user}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWithdrawalLimits(string user)
        {
            using var scope = _logger.BeginScope();

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", user);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["user"] = ["A valid user ID is required."]
                    }).ToActionResult(this);
                }

                // Verify user exists
                var userExists = await _userService.CheckUserExists(userId);
                if (!userExists)
                {
                    _logger.LogWarning("User not found: {UserId}", user);
                    return ResultWrapper.NotFound("User", userId.ToString())
                        .ToActionResult(this);
                }

                // ETag support for caching
                var etagKey = $"withdrawal_limits_user_{user}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    _logger.LogWarning("Already processed.");
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                var result = await _withdrawalService.GetUserWithdrawalLimitsAsync(userId);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch withdrawal limits for user ID {userId}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\"";
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved withdrawal limits for user {UserId}", userId);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving withdrawal limits for user {UserId}", user);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets supported networks for a specific asset
        /// </summary>
        /// <param name="assetTicker">Asset ticker symbol</param>
        /// <returns>List of supported networks for the asset</returns>
        /// <response code="200">Returns the supported networks</response>
        /// <response code="400">If the asset ticker is invalid</response>
        /// <response code="404">If the asset is not found</response>
        [HttpGet("networks/{assetTicker}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSupportedNetworks(string assetTicker)
        {
            using var scope = _logger.BeginScope(new
            {
                AssetTicker = assetTicker
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(assetTicker))
                {
                    _logger.LogWarning("Invalid asset ticker format: {AssetTicker}", assetTicker);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["assetTicker"] = ["A valid asset ticker is required."]
                    }).ToActionResult(this);
                }

                var result = await _withdrawalService.GetSupportedNetworksAsync(assetTicker);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch supported networks for asset {assetTicker}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved {Count} supported networks for asset {AssetTicker}",
                    result.Data?.Count ?? 0, assetTicker);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving supported networks for asset {AssetTicker}: {ErrorMessage}", assetTicker, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets minimum withdrawal amount for a specific asset
        /// </summary>
        /// <param name="assetTicker">Asset ticker symbol</param>
        /// <returns>Minimum withdrawal amount for the asset</returns>
        /// <response code="200">Returns the minimum withdrawal amount</response>
        /// <response code="400">If the asset ticker is invalid</response>
        /// <response code="404">If the asset is not found</response>
        [HttpGet("minimum/{assetTicker}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMinimumWithdrawalAmount(string assetTicker)
        {
            using var scope = _logger.BeginScope(new
            {
                AssetTicker = assetTicker
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(assetTicker))
                {
                    _logger.LogWarning("Invalid asset ticker format: {AssetTicker}", assetTicker);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["assetTicker"] = ["A valid asset ticker is required."]
                    }).ToActionResult(this);
                }

                var result = await _withdrawalService.GetMinimumWithdrawalThresholdAsync(assetTicker);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch minimum withdrawal threshold for asset {assetTicker}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved minimum withdrawal threshold for asset {AssetTicker}: {Amount}",
                    assetTicker, result.Data);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving minimum withdrawal threshold for asset {AssetTicker}: {ErrorMessage}", assetTicker, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets pending withdrawal totals for the authenticated user and specific asset
        /// </summary>
        /// <param name="assetTicker">Asset ticker symbol</param>
        /// <returns>Total pending withdrawal amount for the asset</returns>
        /// <response code="200">Returns the pending withdrawal total</response>
        /// <response code="400">If the asset ticker or user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpGet("total/pending/user/current/asset/{assetTicker}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetPendingTotals(string assetTicker)
        {
            using var scope = _logger.BeginScope(new
            {
                AssetTicker = assetTicker
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(assetTicker))
                {
                    _logger.LogWarning("Invalid asset ticker format: {AssetTicker}", assetTicker);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["assetTicker"] = ["A valid asset ticker is required."]
                    }).ToActionResult(this);
                }

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.GetUserPendingTotalsAsync(userId, assetTicker);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch pending totals for user ID {userId} and asset {assetTicker}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved pending totals for user {UserId} and asset {AssetTicker}: {Amount}",
                    userId, assetTicker, result.Data);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving pending totals for asset {AssetTicker}: {ErrorMessage}", assetTicker, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Checks if the authenticated user can withdraw a specific amount
        /// </summary>
        /// <param name="request">Withdrawal eligibility check request</param>
        /// <returns>Boolean indicating if withdrawal is allowed</returns>
        /// <response code="200">Returns withdrawal eligibility status</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpPost("user/current/can-withdraw")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CanUserWithdraw([FromBody] CanUserWithdrawRequest request)
        {
            using var scope = _logger.BeginScope(new
            {
                Amount = request?.Amount,
                Ticker = request?.Ticker
            });

            try
            {
                // Validate request
                if (request == null)
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["request"] = ["Request body is required."]
                    }).ToActionResult(this);
                }

                if (request.Amount <= 0)
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["amount"] = ["Amount must be greater than zero."]
                    }).ToActionResult(this);
                }

                if (string.IsNullOrEmpty(request.Ticker))
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["ticker"] = ["A valid ticker is required."]
                    }).ToActionResult(this);
                }

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.CanUserWithdrawAsync(userId, request.Amount, request.Ticker);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to check withdrawal eligibility for user ID {userId}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully checked withdrawal eligibility for user {UserId}: {CanWithdraw}",
                    userId, result.Data);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking withdrawal eligibility: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Requests a crypto withdrawal for the authenticated user
        /// </summary>
        /// <param name="request">Crypto withdrawal request details</param>
        /// <returns>Withdrawal receipt with details</returns>
        /// <response code="200">Returns the withdrawal receipt</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized or limits exceeded</response>
        [HttpPost("crypto/request")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> RequestWithdrawal([FromBody] CryptoWithdrawalRequest request)
        {
            using var scope = _logger.BeginScope(new
            {
                WithdrawalMethod = request?.WithdrawalMethod,
                Currency = request?.Currency,
                Amount = request?.Amount
            });

            try
            {
                // Validate request
                if (request == null)
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["request"] = ["Request body is required."]
                    }).ToActionResult(this);
                }

                var validationErrors = new Dictionary<string, string[]>();

                if (request.UserId == Guid.Empty)
                {
                    validationErrors.Add("UserId", new[] { "Invalid user ID." });
                }

                if (string.IsNullOrWhiteSpace(request.WithdrawalMethod) || !WithdrawalMethod.AllValues.Contains(request.WithdrawalMethod))
                {
                    validationErrors.Add("WithdrawalMethod", new[] { "Invalid withdrawal method." });
                }

                if (string.IsNullOrWhiteSpace(request.Currency))
                {
                    validationErrors.Add("Currency", new[] { "Currency is required." });
                }

                if (request.Amount <= 0)
                {
                    validationErrors.Add("Amount", new[] { "Amount must be greater than zero." });
                }

                if (validationErrors.Any())
                {
                    return ResultWrapper.ValidationError(validationErrors, "Validation failed for withdrawal request.")
                        .ToActionResult(this);
                }

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Override the user ID with the authenticated user's ID for security
                request.UserId = userId;

                var result = await _withdrawalService.RequestCryptoWithdrawalAsync(request);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        result.ErrorMessage ?? "Failed to process withdrawal request",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully processed withdrawal request for user {UserId}", userId);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error processing withdrawal request: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Retrieves withdrawal history for the authenticated user
        /// </summary>
        /// <returns>List of user's withdrawal history</returns>
        /// <response code="200">Returns the withdrawal history</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet("history/user/current")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWithdrawalHistory()
        {
            using var scope = _logger.BeginScope();

            try
            {
                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.GetUserWithdrawalHistoryAsync(userId);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch withdrawal history for user ID {userId}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved {Count} withdrawal records for user {UserId}",
                    result.Data?.Count ?? 0, userId);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving withdrawal history: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Retrieves details for a specific withdrawal
        /// </summary>
        /// <param name="withdrawalId">Withdrawal GUID</param>
        /// <returns>Withdrawal details</returns>
        /// <response code="200">Returns the withdrawal details</response>
        /// <response code="400">If the withdrawal ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view this withdrawal</response>
        /// <response code="404">If the withdrawal is not found</response>
        [HttpGet("{withdrawalId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetWithdrawalDetails(string withdrawalId)
        {
            using var scope = _logger.BeginScope(new
            {
                WithdrawalId = withdrawalId
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(withdrawalId) || !Guid.TryParse(withdrawalId, out Guid parsedWithdrawalId) || parsedWithdrawalId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid withdrawal ID format: {WithdrawalId}", withdrawalId);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["withdrawalId"] = ["A valid withdrawal ID is required."]
                    }).ToActionResult(this);
                }

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.GetWithdrawalDetailsAsync(parsedWithdrawalId);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch withdrawal details for ID {parsedWithdrawalId}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                // Ensure user can only see their own withdrawals (unless admin)
                if (result.Data.UserId != userId && !User.IsInRole("ADMIN"))
                {
                    _logger.LogWarning("Unauthorized access attempt to withdrawal {WithdrawalId} by user {UserId}",
                        parsedWithdrawalId, userId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved withdrawal details for ID {WithdrawalId}", parsedWithdrawalId);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving withdrawal details for ID {WithdrawalId}: {ErrorMessage}", withdrawalId, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Cancels a pending withdrawal for the authenticated user
        /// </summary>
        /// <param name="withdrawalId">Withdrawal GUID</param>
        /// <returns>Cancellation result</returns>
        /// <response code="200">Returns success message</response>
        /// <response code="400">If the withdrawal ID is invalid or withdrawal cannot be cancelled</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to cancel this withdrawal</response>
        /// <response code="404">If the withdrawal is not found</response>
        [HttpPut("cancel/{withdrawalId}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelWithdrawal(string withdrawalId)
        {
            using var scope = _logger.BeginScope(new
            {
                WithdrawalId = withdrawalId
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(withdrawalId) || !Guid.TryParse(withdrawalId, out Guid parsedWithdrawalId) || parsedWithdrawalId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid withdrawal ID format: {WithdrawalId}", withdrawalId);
                    return ResultWrapper.ValidationError(new()
                    {
                        ["withdrawalId"] = ["A valid withdrawal ID is required."]
                    }).ToActionResult(this);
                }

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // First get the withdrawal to check ownership
                var getResult = await _withdrawalService.GetWithdrawalDetailsAsync(parsedWithdrawalId);
                if (!getResult.IsSuccess)
                {
                    return ResultWrapper.Failure(getResult.Reason,
                        $"Failed to fetch withdrawal for cancellation check: {getResult.ErrorMessage}",
                        getResult.ErrorCode)
                        .ToActionResult(this);
                }

                // Ensure user can only cancel their own withdrawals (unless admin)
                if (getResult.Data.UserId != userId && !User.IsInRole("ADMIN"))
                {
                    _logger.LogWarning("Unauthorized cancellation attempt for withdrawal {WithdrawalId} by user {UserId}",
                        parsedWithdrawalId, userId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Only pending withdrawals can be canceled
                if (getResult.Data.Status != WithdrawalStatus.Pending)
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["status"] = [$"Cannot cancel withdrawal in {getResult.Data.Status.ToLower()} status"]
                    }, $"Cannot cancel withdrawal in {getResult.Data.Status.ToLower()} status")
                        .ToActionResult(this);
                }

                // Update status to canceled
                var result = await _withdrawalService.UpdateWithdrawalStatusAsync(
                    parsedWithdrawalId,
                    WithdrawalStatus.Cancelled,
                    userId,
                    "Canceled by user");

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        "Failed to cancel withdrawal",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully cancelled withdrawal {WithdrawalId} for user {UserId}", parsedWithdrawalId, userId);

                return ResultWrapper.Success("Withdrawal request canceled successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error canceling withdrawal {WithdrawalId}: {ErrorMessage}", withdrawalId, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        // Admin endpoints

        /// <summary>
        /// Gets pending withdrawal totals for a specific user and asset (Admin only)
        /// </summary>
        /// <param name="userId">User GUID</param>
        /// <param name="assetTicker">Asset ticker symbol</param>
        /// <returns>Total pending withdrawal amount for the user and asset</returns>
        /// <response code="200">Returns the pending withdrawal total</response>
        /// <response code="400">If the user ID or asset ticker is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet("admin/total/pending/user/{userId}/asset/{assetTicker}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPendingTotals(string userId, string assetTicker)
        {
            using var scope = _logger.BeginScope(new
            {
                UserId = userId,
                AssetTicker = assetTicker
            });

            try
            {
                var validationErrors = new Dictionary<string, string[]>();

                // Validate input
                if (string.IsNullOrEmpty(assetTicker))
                {
                    validationErrors.Add("assetTicker", new[] { "A valid asset ticker is required." });
                }

                if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId) || parsedUserId == Guid.Empty)
                {
                    validationErrors.Add("userId", new[] { "A valid user ID is required." });
                }

                if (validationErrors.Any())
                {
                    return ResultWrapper.ValidationError(validationErrors)
                        .ToActionResult(this);
                }

                var userGuid = Guid.Parse(userId);
                // Verify user exists
                var userExists = await _userService.CheckUserExists(userGuid);
                if (!userExists)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    return ResultWrapper.NotFound("User", userId)
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.GetUserPendingTotalsAsync(userGuid, assetTicker);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        $"Failed to fetch pending totals for user ID {userGuid} and asset {assetTicker}",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved pending totals for user {UserId} and asset {AssetTicker}: {Amount}",
                    userGuid, assetTicker, result.Data);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving pending totals for user {UserId} and asset {AssetTicker}: {ErrorMessage}",
                    userId, assetTicker, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets all pending withdrawals with pagination (Admin only)
        /// </summary>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>Paginated list of pending withdrawals</returns>
        /// <response code="200">Returns the pending withdrawals</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized</response>
        [HttpGet("admin/pending")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPendingWithdrawals([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            using var scope = _logger.BeginScope(new
            {
                Page = page,
                PageSize = pageSize
            });

            try
            {
                var result = await _withdrawalService.GetPendingWithdrawalsAsync(page, pageSize);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        "Failed to fetch pending withdrawals",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully retrieved {Count} pending withdrawals (page {Page})",
                    result.Data?.Items?.Count() ?? 0, page);

                return ResultWrapper.Success(result.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving pending withdrawals: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Updates the status of a withdrawal (Admin only)
        /// </summary>
        /// <param name="withdrawalId">Withdrawal GUID</param>
        /// <param name="request">Status update request</param>
        /// <returns>Update result</returns>
        /// <response code="200">Returns success message</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized</response>
        /// <response code="404">If the withdrawal is not found</response>
        [HttpPut("admin/update-status/{withdrawalId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateWithdrawalStatus(
            string withdrawalId,
            [FromBody] WithdrawalStatusUpdateRequest request)
        {
            using var scope = _logger.BeginScope(new
            {
                WithdrawalId = withdrawalId,
                Status = request?.Status
            });

            try
            {
                var validationErrors = new Dictionary<string, string[]>();

                // Validate input
                if (string.IsNullOrEmpty(withdrawalId) || 
                    !Guid.TryParse(withdrawalId, out var parsedWithdrawalId) || 
                    parsedWithdrawalId == Guid.Empty)
                {
                    validationErrors.Add("withdrawalId", new[] { "A valid withdrawal ID is required." });
                }

                if (request == null)
                {
                    validationErrors.Add("request", new[] { "Request body is required." });
                }
                else if (string.IsNullOrEmpty(request.Status))
                {
                    validationErrors.Add("status", new[] { "Status is required." });
                }

                if (validationErrors.Any())
                {
                    return ResultWrapper.ValidationError(validationErrors)
                        .ToActionResult(this);
                }

                var withdrawalGuid= Guid.Parse(withdrawalId);
                // Authorization check - get admin user ID
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid adminUserId) || adminUserId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid admin user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var result = await _withdrawalService.UpdateWithdrawalStatusAsync(
                    withdrawalGuid,
                    request.Status,
                    adminUserId,
                    request.Comment,
                    request.TransactionHash);

                if (!result.IsSuccess)
                {
                    return ResultWrapper.Failure(result.Reason,
                        "Failed to update withdrawal status",
                        result.ErrorCode)
                        .ToActionResult(this);
                }

                _logger.LogInformation("Successfully updated withdrawal {WithdrawalId} status to {Status}",
                    withdrawalGuid, request.Status);

                return ResultWrapper.Success("Withdrawal status updated successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error updating withdrawal status for ID {WithdrawalId}: {ErrorMessage}", withdrawalId, ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }
    }
}