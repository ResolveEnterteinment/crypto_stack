using Application.Contracts.Requests.Subscription;
using Application.Interfaces;
using Application.Validation;
using Domain.Exceptions;
using FluentValidation;
using Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubscriptionController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IValidator<SubscriptionCreateRequest> _createValidator;
        private readonly IValidator<SubscriptionUpdateRequest> _updateValidator;
        private readonly ILogger<SubscriptionController> _logger;
        private readonly IIdempotencyService _idempotencyService;

        public SubscriptionController(
            ISubscriptionService subscriptionService,
            IValidator<SubscriptionCreateRequest> createValidator,
            IValidator<SubscriptionUpdateRequest> updateValidator,
            IIdempotencyService idempotencyService,
            ILogger<SubscriptionController> logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _createValidator = createValidator ?? throw new ArgumentNullException(nameof(createValidator));
            _updateValidator = updateValidator ?? throw new ArgumentNullException(nameof(updateValidator));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpPost]
        [Route("user/{user}")]
        [Authorize]
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
                    if (string.IsNullOrEmpty(user) || !Guid.TryParse(user, out Guid userId) || userId == Guid.Empty)
                    {
                        throw new Domain.Exceptions.ValidationException("Invalid user ID", new Dictionary<string, string[]>
                        {
                            ["UserId"] = new[] { "A valid user ID is required." }
                        });
                    }

                    var subscriptionsResult = await _subscriptionService.GetAllByUserIdAsync(userId);

                    if (!subscriptionsResult.IsSuccess)
                    {
                        throw new DatabaseException(subscriptionsResult.ErrorMessage);
                    }

                    return Ok(subscriptionsResult.Data);
                }
                catch (Exception ex)
                {
                    // The global exception handler middleware will handle this
                    _logger.LogError(ex, "Error retrieving subscriptions for user {UserId}", user);
                    throw;
                }
            }
        }

        [HttpPost]
        [Route("new")]
        [Authorize]
        public async Task<IActionResult> New([FromBody] SubscriptionCreateRequest subscriptionRequest,
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
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        throw new Domain.Exceptions.ValidationException("Missing idempotency key", new Dictionary<string, string[]>
                        {
                            ["X-Idempotency-Key"] = new[] { "Idempotency key is required for subscription creation." }
                        });
                    }

                    // Check for existing operation with this idempotency key
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        var existingResult = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                        return Ok($"Subscription #{existingResult} successfully created (from previous request).");
                    }

                    // Validate the request
                    await _createValidator.ValidateAndThrowAsync(subscriptionRequest);

                    // Process the request
                    var subscriptionResult = await _subscriptionService.Create(subscriptionRequest);

                    if (!subscriptionResult.IsSuccess)
                    {
                        throw new DomainException(subscriptionResult.ErrorMessage, "SUBSCRIPTION_CREATION_FAILED");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, subscriptionResult.Data);

                    return Ok($"Subscription #{subscriptionResult.Data} successfully created.");
                }
                catch (Exception ex)
                {
                    // The global exception handler middleware will handle this
                    _logger.LogError(ex, "Error creating subscription for user {UserId}", subscriptionRequest?.UserId);
                    throw;
                }
            }
        }

        [HttpPost]
        [Route("update/{id}")]
        [Authorize]
        public async Task<IActionResult> Update([FromBody] SubscriptionUpdateRequest updateRequest, string id,
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
                    if (!Guid.TryParse(id, out var subscriptionId))
                    {
                        throw new Domain.Exceptions.ValidationException("Invalid subscription ID", new Dictionary<string, string[]>
                        {
                            ["SubscriptionId"] = new[] { "A valid subscription ID is required." }
                        });
                    }

                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        throw new Domain.Exceptions.ValidationException("Missing idempotency key", new Dictionary<string, string[]>
                        {
                            ["X-Idempotency-Key"] = new[] { "Idempotency key is required for subscription updates." }
                        });
                    }

                    // Check for existing operation with this idempotency key
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        var existingResult = await _idempotencyService.GetResultAsync<long>(idempotencyKey);
                        return Ok($"Subscription #{id} updated successfully (from previous request).");
                    }

                    // Validate the request
                    await _updateValidator.ValidateAndThrowAsync(updateRequest);

                    // Process the request
                    var subscriptionUpdateResult = await _subscriptionService.Update(subscriptionId, updateRequest);

                    if (!subscriptionUpdateResult.IsSuccess)
                    {
                        throw new DomainException(subscriptionUpdateResult.ErrorMessage, "SUBSCRIPTION_UPDATE_FAILED");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, subscriptionUpdateResult.Data);

                    return Ok($"Subscription #{subscriptionUpdateResult.Data} updated successfully.");
                }
                catch (Exception ex)
                {
                    // The global exception handler middleware will handle this
                    _logger.LogError(ex, "Error updating subscription {SubscriptionId}", id);
                    throw;
                }
            }
        }

        [HttpPost]
        [Route("cancel/{id}")]
        [Authorize]
        public async Task<IActionResult> Cancel(string id, [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
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
                    if (!Guid.TryParse(id, out var subscriptionId))
                    {
                        throw new Domain.Exceptions.ValidationException("Invalid subscription ID", new Dictionary<string, string[]>
                        {
                            ["SubscriptionId"] = new[] { "A valid subscription ID is required." }
                        });
                    }

                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        throw new Domain.Exceptions.ValidationException("Missing idempotency key", new Dictionary<string, string[]>
                        {
                            ["X-Idempotency-Key"] = new[] { "Idempotency key is required for subscription cancellation." }
                        });
                    }

                    // Check for existing operation with this idempotency key
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        return Ok($"Subscription #{id} has been cancelled (from previous request).");
                    }

                    // Process the request
                    var result = await _subscriptionService.CancelAsync(subscriptionId);

                    if (!result.IsAcknowledged)
                    {
                        throw new DatabaseException($"Failed to cancel subscription {id}");
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, true);

                    return Ok($"Subscription #{id} has been cancelled.");
                }
                catch (Exception ex)
                {
                    // The global exception handler middleware will handle this
                    _logger.LogError(ex, "Error cancelling subscription {SubscriptionId}", id);
                    throw;
                }
            }
        }
    }
}