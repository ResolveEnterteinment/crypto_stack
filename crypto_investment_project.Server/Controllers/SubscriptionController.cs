using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IValidator<SubscriptionCreateRequest> _createValidator;
        private readonly IValidator<SubscriptionUpdateRequest> _updateValidator;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IUserService _userService;

        public SubscriptionController(
            ISubscriptionService subscriptionService,
            IValidator<SubscriptionCreateRequest> createValidator,
            IValidator<SubscriptionUpdateRequest> updateValidator,
            IIdempotencyService idempotencyService,
            IUserService userService,
            ILogger<SubscriptionController> logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _createValidator = createValidator ?? throw new ArgumentNullException(nameof(createValidator));
            _updateValidator = updateValidator ?? throw new ArgumentNullException(nameof(updateValidator));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all subscriptions for a specific user
        /// </summary>
        /// <param name="user">User GUID</param>
        /// <returns>Collection of subscriptions belonging to the user</returns>
        /// <response code="200">Returns the user's subscriptions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpPost]
        [Route("user/{user}")]
        [IgnoreAntiforgeryToken]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetByUser(string user)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["UserId"] = user,
                ["Operation"] = "GetSubscriptionsByUser",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    // Validate input
                    if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out Guid userId) || userId == Guid.Empty)
                    {
                        _logger.LogWarning("Invalid user ID format: {UserId}", user);
                        return BadRequest(new { message = "A valid user ID is required." });
                    }

                    // Verify user exists
                    var userExists = await _userService.CheckUserExists(userId);
                    if (!userExists)
                    {
                        _logger.LogWarning("User not found: {UserId}", user);
                        return NotFound(new { message = "User not found" });
                    }

                    // Authorization check - verify current user can access this data
                    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                    if (!User.IsInRole("ADMIN") && currentUserId != user)
                    {
                        _logger.LogWarning("Unauthorized access attempt to user {TargetUserId} subscriptions by user {CurrentUserId}",
                            user, currentUserId);
                        return Forbid();
                    }

                    // ETag support for caching
                    var etagKey = $"subscriptions_user_{user}";
                    var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                    var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                    if (hasEtag && etag == storedEtag)
                    {
                        return StatusCode(StatusCodes.Status304NotModified);
                    }

                    // Get user subscriptions
                    var subscriptionsResult = await _subscriptionService.GetAllByUserIdAsync(userId);

                    if (!subscriptionsResult.IsSuccess)
                    {
                        throw new DatabaseException(subscriptionsResult.ErrorMessage);
                    }

                    // Generate ETag from data and store it
                    var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash
                    await _idempotencyService.StoreResultAsync(etagKey, newEtag);
                    Response.Headers.ETag = newEtag;

                    _logger.LogInformation("Successfully retrieved {Count} subscriptions for user {UserId}",
                        subscriptionsResult.Data?.Count() ?? 0, userId);

                    return Ok(subscriptionsResult.Data);
                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", user);
                    throw;
                }
            }
        }

        /// <summary>
        /// Creates a new subscription
        /// </summary>
        /// <param name="subscriptionRequest">The subscription details</param>
        /// <param name="idempotencyKey">Unique key to prevent duplicate operations</param>
        /// <returns>The ID of the created subscription</returns>
        /// <response code="201">Returns the newly created subscription ID</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to create a subscription</response>
        /// <response code="409">If an idempotent request was already processed</response>
        /// <response code="422">If the request validation fails</response>
        [HttpPost]
        [Route("new")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "USER")]
        [EnableRateLimiting("heavyOperations")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> New(
            [FromBody] SubscriptionCreateRequest subscriptionRequest,
            [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["UserId"] = subscriptionRequest?.UserId,
                ["Operation"] = "CreateSubscription",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ["IdempotencyKey"] = idempotencyKey
            }))
            {
                try
                {
                    // Check idempotency key
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        _logger.LogWarning("Missing idempotency key in subscription create request");
                        return BadRequest(new { message = "X-Idempotency-Key header is required for subscription creation" });
                    }

                    // Check for existing operation with this idempotency key
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Idempotent request detected: {IdempotencyKey}, subscription ID: {SubscriptionId}",
                            idempotencyKey, existingResult);
                        return StatusCode(StatusCodes.Status409Conflict,
                            new { message = "Request already processed", subscriptionId = existingResult });
                    }

                    // Authorization check - verify current user can create this subscription
                    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var isAdmin = User.IsInRole("ADMIN");

                    if (!isAdmin && subscriptionRequest?.UserId != currentUserId)
                    {
                        _logger.LogWarning("Unauthorized attempt to create subscription for user {TargetUserId} by user {CurrentUserId}",
                            subscriptionRequest?.UserId, currentUserId);
                        return Forbid();
                    }

                    // Validate request
                    var validationResult = await _createValidator.ValidateAsync(subscriptionRequest);
                    if (!validationResult.IsValid)
                    {
                        var errors = validationResult.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(
                                g => g.Key,
                                g => g.Select(e => e.ErrorMessage).ToArray()
                            );

                        _logger.LogWarning("Validation failed for subscription creation: {ValidationErrors}",
                            string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

                        return UnprocessableEntity(new { message = "Validation failed", errors });
                    }

                    // Process the request
                    var subscriptionCreateResult = await _subscriptionService.CreateAsync(subscriptionRequest);

                    if (!subscriptionCreateResult.IsSuccess)
                    {
                        throw new DomainException(subscriptionCreateResult.ErrorMessage, "SUBSCRIPTION_CREATION_FAILED");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, subscriptionCreateResult.Data);

                    _logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}",
                        subscriptionCreateResult.Data, subscriptionRequest.UserId);

                    // Return 201 Created with the location header
                    return CreatedAtAction(
                        nameof(GetByUser),
                        new { user = subscriptionRequest.UserId, version = HttpContext.GetRequestedApiVersion()?.ToString() ?? "1.0" },
                        new { id = subscriptionCreateResult.Data, message = "Subscription created successfully" }
                    );
                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error creating subscription for user {UserId}", subscriptionRequest?.UserId);
                    throw;
                }
            }
        }

        /// <summary>
        /// Updates an existing subscription
        /// </summary>
        /// <param name="updateRequest">The updated subscription details</param>
        /// <param name="id">The ID of the subscription to update</param>
        /// <param name="idempotencyKey">Unique key to prevent duplicate operations</param>
        /// <returns>Success status</returns>
        /// <response code="200">If the subscription was updated successfully</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to update this subscription</response>
        /// <response code="404">If the subscription is not found</response>
        /// <response code="409">If an idempotent request was already processed</response>
        /// <response code="422">If the request validation fails</response>
        [HttpPut]
        [Route("update/{id}")]
        [Authorize]
        [EnableRateLimiting("heavyOperations")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Update(
            [FromBody] SubscriptionUpdateRequest updateRequest,
            [FromRoute] string id,
            [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["SubscriptionId"] = id,
                ["Operation"] = "UpdateSubscription",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ["IdempotencyKey"] = idempotencyKey
            }))
            {
                try
                {
                    // Validate subscription ID
                    if (!Guid.TryParse(id, out var subscriptionId))
                    {
                        _logger.LogWarning("Invalid subscription ID format: {SubscriptionId}", id);
                        return BadRequest(new { message = "A valid subscription ID is required" });
                    }

                    // Check idempotency key
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        _logger.LogWarning("Missing idempotency key in subscription update request");
                        return BadRequest(new { message = "X-Idempotency-Key header is required for subscription updates" });
                    }

                    // Check for existing operation with this idempotency key
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<long>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Idempotent request detected: {IdempotencyKey}, subscription ID: {SubscriptionId}",
                            idempotencyKey, id);
                        return StatusCode(StatusCodes.Status409Conflict,
                            new { message = "Request already processed" });
                    }

                    // Get the subscription to check ownership
                    var subscription = await _subscriptionService.GetByIdAsync(subscriptionId);
                    if (subscription == null)
                    {
                        _logger.LogWarning("Subscription not found: {SubscriptionId}", id);
                        return NotFound(new { message = "Subscription not found" });
                    }

                    // Authorization check - verify current user owns this subscription or is admin
                    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var isAdmin = User.IsInRole("ADMIN");

                    if (!isAdmin && subscription.UserId.ToString() != currentUserId)
                    {
                        _logger.LogWarning("Unauthorized attempt to update subscription {SubscriptionId} by user {CurrentUserId}",
                            id, currentUserId);
                        return Forbid();
                    }

                    // Validate request
                    var validationResult = await _updateValidator.ValidateAsync(updateRequest);
                    if (!validationResult.IsValid)
                    {
                        var errors = validationResult.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(
                                g => g.Key,
                                g => g.Select(e => e.ErrorMessage).ToArray()
                            );

                        _logger.LogWarning("Validation failed for subscription update: {ValidationErrors}",
                            string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

                        return UnprocessableEntity(new { message = "Validation failed", errors });
                    }

                    // Process the request
                    var subscriptionUpdateResult = await _subscriptionService.UpdateAsync(subscriptionId, updateRequest);

                    if (!subscriptionUpdateResult.IsSuccess)
                    {
                        throw new DomainException(subscriptionUpdateResult.ErrorMessage, "SUBSCRIPTION_UPDATE_FAILED");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, subscriptionUpdateResult.Data);

                    _logger.LogInformation("Successfully updated subscription {SubscriptionId}", id);

                    return Ok(new
                    {
                        message = "Subscription updated successfully",
                        modifiedCount = subscriptionUpdateResult.Data
                    });
                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error updating subscription {SubscriptionId}", id);
                    throw;
                }
            }
        }

        /// <summary>
        /// Cancels an existing subscription
        /// </summary>
        /// <param name="id">The ID of the subscription to cancel</param>
        /// <param name="idempotencyKey">Unique key to prevent duplicate operations</param>
        /// <returns>Success status</returns>
        /// <response code="200">If the subscription was cancelled successfully</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to cancel this subscription</response>
        /// <response code="404">If the subscription is not found</response>
        /// <response code="409">If an idempotent request was already processed</response>
        [HttpPost]
        [Route("cancel/{id}")]
        [Authorize]
        [EnableRateLimiting("standard")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Cancel(
            [FromRoute] string id,
            [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["SubscriptionId"] = id,
                ["Operation"] = "CancelSubscription",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ["IdempotencyKey"] = idempotencyKey
            }))
            {
                try
                {
                    // Validate subscription ID
                    if (!Guid.TryParse(id, out var subscriptionId))
                    {
                        _logger.LogWarning("Invalid subscription ID format: {SubscriptionId}", id);
                        return BadRequest(new { message = "A valid subscription ID is required" });
                    }

                    // Check idempotency key
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        _logger.LogWarning("Missing idempotency key in subscription cancellation request");
                        return BadRequest(new { message = "X-Idempotency-Key header is required for subscription cancellation" });
                    }

                    // Check for existing operation with this idempotency key
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        _logger.LogInformation("Idempotent request detected: {IdempotencyKey}, subscription ID: {SubscriptionId}",
                            idempotencyKey, id);
                        return StatusCode(StatusCodes.Status409Conflict,
                            new { message = "Request already processed" });
                    }

                    // Get the subscription to check ownership
                    var subscription = await _subscriptionService.GetByIdAsync(subscriptionId);
                    if (subscription == null)
                    {
                        _logger.LogWarning("Subscription not found: {SubscriptionId}", id);
                        return NotFound(new { message = "Subscription not found" });
                    }

                    // Authorization check - verify current user owns this subscription or is admin
                    var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    var isAdmin = User.IsInRole("ADMIN");

                    if (!isAdmin && subscription.UserId.ToString() != currentUserId)
                    {
                        _logger.LogWarning("Unauthorized attempt to cancel subscription {SubscriptionId} by user {CurrentUserId}",
                            id, currentUserId);
                        return Forbid();
                    }

                    // Process the request
                    var result = await _subscriptionService.CancelAsync(subscriptionId);

                    if (!result.IsAcknowledged)
                    {
                        throw new DatabaseException($"Failed to cancel subscription {id}");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, true);

                    _logger.LogInformation("Successfully cancelled subscription {SubscriptionId}", id);

                    return Ok(new { message = "Subscription has been cancelled" });
                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error cancelling subscription {SubscriptionId}", id);
                    throw;
                }
            }
        }
    }
}