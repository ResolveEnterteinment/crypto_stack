using Application.Contracts.Requests.Subscription;
using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.DTOs;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class TransactionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly ITransactionService _transactionService;
        private readonly ILoggingService _logger;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IUserService _userService;

        public TransactionController(
            ISubscriptionService subscriptionService,
            ITransactionService transactionService,
            IValidator<SubscriptionCreateRequest> createValidator,
            IValidator<SubscriptionUpdateRequest> updateValidator,
            IIdempotencyService idempotencyService,
            IUserService userService,
            ILoggingService logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _transactionService = transactionService ?? throw new ArgumentNullException(nameof(transactionService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all transactions for the authenticated user
        /// </summary>
        /// <param name="user">User GUID</param>
        /// <returns>Collection of transactions belonging to the user</returns>
        /// <response code="200">Returns the user's transactions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet]
        [Route("user")]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetByCurrentUser()
        {
            using var scope = _logger.BeginScope();

            try
            {
                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if(string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // ETag support for caching
                var etagKey = $"transactions_user_{userId}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    _logger.LogWarning("Already processed.");

                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                // Get user subscriptions
                var transactionsResult = await _transactionService.GetUserTransactionsAsync(userId);

                if (!transactionsResult.IsSuccess)
                {
                    return ResultWrapper.Failure(transactionsResult.Reason, 
                        $"Failed to fetch transactions for user ID {userId}", 
                        transactionsResult.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved {Count} transactions for user {UserId}",
                    transactionsResult.Data?.Items.Count() ?? 0, userId);

                return ResultWrapper.Success(transactionsResult.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                // Let global exception handler middleware handle this
                _logger.LogError("Error retrieving transactions: {ErrorMessage}", ex.Message);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }

        }

        /// <summary>
        /// Retrieves all transactions for a specific user
        /// </summary>
        /// <param name="user">User GUID</param>
        /// <returns>Collection of transactions belonging to the user</returns>
        /// <response code="200">Returns the user's transactions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet]
        [Route("admin/user/{user}")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetByUser(string user)
        {
            using var scope = _logger.BeginScope();

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out Guid userId) || userId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid user ID format: {UserId}", user);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
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
                var etagKey = $"transactions_user_{user}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    _logger.LogWarning("Already processed.");
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                // Get user subscriptions
                var transactionsResult = await _transactionService.GetUserTransactionsAsync(userId);

                if (!transactionsResult.IsSuccess)
                {
                    return ResultWrapper.Failure(transactionsResult.Reason,
                        $"Failed to fetch transactions for user ID {userId}",
                        transactionsResult.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved {Count} transactions for user {UserId}",
                    transactionsResult.Data?.Items.Count() ?? 0, userId);

                return ResultWrapper.Success(transactionsResult.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                // Let global exception handler middleware handle this
                _logger.LogError("Error retrieving transactions for user {UserId}", user);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }

        }

        /// <summary>
        /// Retrieves all transactions for a specific subcription belonging to the authenticated user
        /// </summary>
        /// <param name="subscription">Subcription GUID</param>
        /// <returns>Collection of transactions belonging to the subcription</returns>
        /// <response code="200">Returns the subcription transactions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet]
        [Route("subscription/{subscription}")]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetByOwnersSubscriptionId(string subscription)
        {
            using var scope = _logger.BeginScope(new
            {
                SubscriptionId = subscription
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(subscription) || !Guid.TryParse(subscription, out Guid subscriptionId) || subscriptionId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid subscription ID format: {SubscriptionId}", subscription);

                    return ResultWrapper.ValidationError(new()
                    {
                        ["subscription"] = ["A valid subscription ID is required."]
                    }).ToActionResult(this);
                }

                // Verify user exists
                var subscriptionResult = await _subscriptionService.GetByIdAsync(subscriptionId);
                if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                {
                    _logger.LogWarning("Subscription not found: {SubscriptionId}", subscriptionId);
                    return ResultWrapper.NotFound("Subscription", subscriptionId.ToString())
                        .ToActionResult(this);
                }

                var subscriptionData = subscriptionResult.Data;

                // Authorization check - verify current user can access this data
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (currentUserId != subscriptionData.UserId.ToString())
                {
                    _logger.LogWarning("Unauthorized access attempt to user {TargetUserId} transactions by user {CurrentUserId}",
                        subscriptionData.UserId, currentUserId);
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // ETag support for caching
                var etagKey = $"transactions_subscription_{subscription}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                // Get user subscriptions
                var transactionsResult = await _transactionService.GetBySubscriptionIdAsync(subscriptionId);

                if (!transactionsResult.IsSuccess)
                {
                    return ResultWrapper.Failure(transactionsResult.Reason,
                        $"Failed to fetch transactions for subscription ID {subscriptionId}",
                        transactionsResult.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved {Count} transactions for subscription {SubscriptionId}",
                    transactionsResult.Data?.Count() ?? 0, subscriptionId);

                return ResultWrapper.Success(transactionsResult.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                // Let global exception handler middleware handle this
                _logger.LogError("Error retrieving transactions for subscription {SubscriptionId}", subscription);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Retrieves all transactions for a specific subcription
        /// </summary>
        /// <param name="subscription">Subcription GUID</param>
        /// <returns>Collection of transactions belonging to the subcription</returns>
        /// <response code="200">Returns the subcription transactions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet]
        [Route("admin/subscription/{subscription}")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBySubscriptionId(string subscription)
        {
            using var scope = _logger.BeginScope(new
            {
                SubscriptionId = subscription
            });

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(subscription) || !Guid.TryParse(subscription, out Guid subscriptionId) || subscriptionId == Guid.Empty)
                {
                    _logger.LogWarning("Invalid subscription ID format: {SubscriptionId}", subscription);

                    return ResultWrapper.ValidationError(new()
                    {
                        ["subscription"] = ["A valid subscription ID is required."]
                    }).ToActionResult(this);
                }

                // Verify subscription exists
                var subscriptionResult = await _subscriptionService.GetByIdAsync(subscriptionId);
                if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                {
                    _logger.LogWarning("Subscription not found: {SubscriptionId}", subscriptionId);
                    return ResultWrapper.NotFound("Subscription", subscriptionId.ToString())
                        .ToActionResult(this);
                }

                var subscriptionData = subscriptionResult.Data;

                // ETag support for caching
                var etagKey = $"transactions_subscription_{subscription}";
                var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                if (hasEtag && etag == storedEtag)
                {
                    return ResultWrapper.Failure(FailureReason.ConcurrencyConflict,
                            "Request already processed", "DUPLICATE_REQUEST")
                            .ToActionResult(this);
                }

                // Get user subscriptions
                var transactionsResult = await _transactionService.GetBySubscriptionIdAsync(subscriptionId);

                if (!transactionsResult.IsSuccess)
                {
                    return ResultWrapper.Failure(transactionsResult.Reason,
                        $"Failed to fetch transactions for subscription ID {subscriptionId}",
                        transactionsResult.ErrorCode)
                        .ToActionResult(this);
                }

                // Generate ETag from data and store it
                var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash
                await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                Response.Headers.ETag = newEtag;

                _logger.LogInformation("Successfully retrieved {Count} transactions for subscription {SubscriptionId}",
                    transactionsResult.Data?.Count() ?? 0, subscriptionId);

                return ResultWrapper.Success(transactionsResult.Data)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                // Let global exception handler middleware handle this
                _logger.LogError("Error retrieving transactions for subscription {SubscriptionId}", subscription);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }
    }
}