using Application.Contracts.Requests.Asset;
using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Asset;
using Domain.Constants;
using Domain.DTOs;
using Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class AssetController : ControllerBase
    {
        private readonly IAssetService _assetService;
        private readonly IValidator<AssetCreateRequest> _createValidator;
        private readonly IValidator<AssetUpdateRequest> _updateValidator;
        private readonly ILogger<AssetController> _logger;
        private readonly IIdempotencyService _idempotencyService;

        public AssetController(
            IAssetService assetService,
            IValidator<AssetCreateRequest> createValidator,
            IValidator<AssetUpdateRequest> updateValidator,
            IIdempotencyService idempotencyService,
            IUserService userService,
            ILogger<AssetController> logger)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _createValidator = createValidator ?? throw new ArgumentNullException(nameof(createValidator));
            _updateValidator = updateValidator ?? throw new ArgumentNullException(nameof(updateValidator));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves all supported assets on the platform
        /// </summary>
        /// <param name="user">User GUID</param>
        /// <returns>Collection of subscriptions belonging to the user</returns>
        /// <response code="200">Returns the user's subscriptions</response>
        /// <response code="400">If the user ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view these subscriptions</response>
        /// <response code="404">If the user is not found</response>
        [HttpGet]
        [Route("get/supported")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetSupportedAssets()
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "GetSupportedAssets",
            }))
            {
                try
                {
                    // ETag support for caching
                    var etagKey = $"supported_assets";
                    var etag = Request.Headers.IfNoneMatch.FirstOrDefault();
                    var (hasEtag, storedEtag) = await _idempotencyService.GetResultAsync<string>(etagKey);

                    if (hasEtag && etag == storedEtag)
                    {
                        return StatusCode(StatusCodes.Status304NotModified);
                    }

                    // Get supported assets
                    var assetsResult = await _assetService.GetSupportedAssetsAsync();

                    if (!assetsResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(assetsResult.Reason, 
                            "Failed to retrieve supported assets", 
                            "ASSET_RETRIEVAL_FAILED")
                            .ToActionResult(this);
                    }

                    // Generate ETag from data and store it
                    var newEtag = $"\"{Guid.NewGuid():N}\""; // Simple approach; production would use content hash

                    await _idempotencyService.StoreResultAsync(etagKey, newEtag);

                    Response.Headers.ETag = newEtag;

                    _logger.LogInformation("Successfully retrieved {Count} assets.",
                        assetsResult.Data?.Count() ?? 0);

                    return ResultWrapper.Success(assetsResult.Data, "Successfully retrieved supported assets.")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving supported assets");
                    return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Creates a new subscription
        /// </summary>
        /// <param name="assetCreateRequest">The subscription details</param>
        /// <param name="idempotencyKey">Unique key to prevent duplicate operations</param>
        /// <returns>The ID of the created subscription</returns>
        /// <response code="201">Returns the newly created subscription ID</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to create a subscription</response>
        /// <response code="409">If an idempotent request was already processed</response>
        /// <response code="422">If the request validation fails</response>
        [HttpPost]
        [Route("admin/new")]
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("heavyOperations")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> New(
            [FromBody] AssetCreateRequest assetCreateRequest,
            [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["AssetName"] = assetCreateRequest?.Name,
                ["Operation"] = "CreateAsset",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ["IdempotencyKey"] = idempotencyKey
            }))
            {
                try
                {
                    // Check idempotency key
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        _logger.LogWarning("Missing idempotency key in asset create request");

                        return ResultWrapper.Failure(FailureReason.ValidationError, 
                            "X-Idempotency-Key header is required for asset creation", 
                            "INVALID_REQUEST")
                            .ToActionResult(this);
                    }

                    // Check for existing operation with this idempotency key
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<Guid>(idempotencyKey);
                    if (resultExists)
                    {
                        _logger.LogInformation("Idempotent request detected: {IdempotencyKey}, asset ID: {AssetId}",
                            idempotencyKey, existingResult);

                        return ResultWrapper.Failure(FailureReason.ConcurrencyConflict, 
                            "Asset create request already processed", 
                            "ASSET_CREATION_CONFLICT")
                            .ToActionResult(this);
                    }

                    // Validate request
                    var validationResult = await _createValidator.ValidateAsync(assetCreateRequest);
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

                        return ResultWrapper.Failure(FailureReason.ValidationError, 
                            "Validation failed", 
                            "INVALID_REQUEST",
                            errors)
                            .ToActionResult(this);
                    }

                    // Process the request
                    var assetCreateResult = await _assetService.CreateAsync(assetCreateRequest);

                    if (!assetCreateResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(assetCreateResult.Reason,
                            $"Failed to create asset: {assetCreateResult.ErrorMessage}",
                            "ASSET_CREATION_FAILED")
                            .ToActionResult(this);
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, assetCreateResult.Data);

                    _logger.LogInformation("Successfully created asset {AssetId}",
                        assetCreateResult.Data);

                    return ResultWrapper.Success(
                        $"Asset {assetCreateResult.Data} created successfully.")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error creating asset {Name}({Ticker})", assetCreateRequest?.Name, assetCreateRequest?.Ticker);

                    return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
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
        [Authorize(Roles = "ADMIN")]
        [EnableRateLimiting("heavyOperations")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Update(
            [FromBody] AssetUpdateRequest updateRequest,
            [FromRoute] string id,
            [FromHeader(Name = "X-Idempotency-Key")] string idempotencyKey)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["AssetId"] = id,
                ["Operation"] = "UpdateAsset",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ["IdempotencyKey"] = idempotencyKey
            }))
            {
                try
                {
                    // Validate asset ID
                    if (!Guid.TryParse(id, out var assetId))
                    {
                        _logger.LogWarning("Invalid asset ID format: {AssetId}", id);
                        return ResultWrapper.Failure(FailureReason.ValidationError, 
                            "Invalid asset ID format", 
                            "INVALID_REQUEST").ToActionResult(this);
                    }

                    // Check idempotency key
                    if (string.IsNullOrEmpty(idempotencyKey))
                    {
                        _logger.LogWarning("Missing idempotency key in asset update request");
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "X-Idempotency-Key header is required for asset updates",
                            "INVALID_REQUEST").ToActionResult(this);
                    }

                    // Check for existing operation with this idempotency key
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<long>(idempotencyKey);
                    
                    if (resultExists)
                    {
                        _logger.LogInformation("Idempotent request detected: {IdempotencyKey}, asset ID: {AssetId}",
                            idempotencyKey, id);

                        return ResultWrapper.Failure(FailureReason.ConcurrencyConflict, 
                            "Asset update request already processed", 
                            "ASSET_UPDATE_CONFLICT").ToActionResult(this);
                    }

                    // Get the subscription to check ownership
                    var assetResult = await _assetService.GetByIdAsync(assetId);
                    if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                    {
                        _logger.LogWarning("Asset not found: {AssetId}", id);

                        return ResultWrapper.NotFound("Asset", id).ToActionResult(this);
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

                        _logger.LogWarning("Validation failed for asset update: {ValidationErrors}",
                            string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

                        return ResultWrapper.Failure(FailureReason.ValidationError, 
                            "Validation failed",
                            "INVALID_REQUEST",
                            errors)
                            .ToActionResult(this);
                    }

                    // Process the request
                    var updateResult = await _assetService.UpdateAsync(assetId, updateRequest);

                    if (updateResult == null || !updateResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(updateResult.Reason,
                            $"Failed to update asset ID {assetId}",
                            "ASSET_UPDATE_FAILED")
                            .ToActionResult(this);
                    }

                    // Store the result for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, assetId);

                    _logger.LogInformation("Successfully updated subscription {SubscriptionId}", assetId);

                    return ResultWrapper.Success(
                        $"Asset {assetId} updated successfully. Modified count: {updateResult.Data.ModifiedCount}")
                        .ToActionResult(this);

                }
                catch (Exception ex)
                {
                    // Let global exception handler middleware handle this
                    _logger.LogError(ex, "Error updating subscription {SubscriptionId}", id);

                    return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
                }
            }
        }
    }
}