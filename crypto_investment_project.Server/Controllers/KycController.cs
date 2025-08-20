using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Application.Extensions;
using Application.Interfaces.KYC;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/kyc")]
    [Authorize]
    [Produces("application/json")]
    [EnableRateLimiting("KycEndpoints")]
    public class KycController : ControllerBase
    {
        private readonly IKycService _kycService;
        private readonly IKycSessionService _kycSessionService;
        private readonly IDocumentService _documentService;
        private readonly ILiveCaptureService _liveCaptureService;
        private readonly ILogger<KycController> _logger;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly IAntiforgery _antiforgery;
        private readonly IWebHostEnvironment _environment;

        private readonly string[] _allowedFileTypes = { ".jpg", ".jpeg", ".png", ".pdf", ".webp" };
        private readonly long _maxFileSize = 10 * 1024 * 1024; // 10MB

        public KycController(
            IKycService kycService,
            IKycSessionService kycSessionService,
            ILogger<KycController> logger,
            IMemoryCache cache,
            IConfiguration configuration,
            IAntiforgery antiforgery,
            IWebHostEnvironment environment,
            IDocumentService documentService,
            ILiveCaptureService liveCaptureService)
        {
            _kycService = kycService ?? throw new ArgumentNullException(nameof(kycService));
            _kycSessionService = kycSessionService ?? throw new ArgumentNullException(nameof(kycSessionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _liveCaptureService = liveCaptureService ?? throw new ArgumentNullException(nameof(liveCaptureService));
        }

        /// <summary>
        /// Get current KYC status for the authenticated user
        /// </summary>
        /// <returns>Current KYC status information</returns>
        /// <response code="200">Returns the user's KYC status</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpGet("status")]
        [ProducesResponseType(typeof(KycStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetKycStatus()
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "GetKycStatus",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Check cache first
                    var cacheKey = $"kyc_status_{userId}";

                    if (_cache.TryGetValue(cacheKey, out KycStatusResponse? cachedStatus) && cachedStatus != null)
                    {
                        _logger.LogInformation("KYC status retrieved from cache for user {UserId}", userId);
                        return ResultWrapper.Success(cachedStatus, "KYC status retrieved from cache")
                            .ToActionResult(this);
                    }

                    var result = await _kycService.GetUserKycStatusAsync(userId.Value);

                    if (!result.IsSuccess)
                    {
                        throw new Exception($"Failed to retrieve KYC status: {result.ErrorMessage}");
                    }

                    var statusResponse = MapToStatusResponse(result.Data);

                    // Cache for 5 minutes
                    _cache.Set(cacheKey, statusResponse, TimeSpan.FromMinutes(5));

                    _logger.LogInformation("KYC status retrieved successfully for user {UserId}", userId);

                    return ResultWrapper.Success(statusResponse, "KYC status retrieved successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving KYC status");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Create a new KYC verification session
        /// </summary>
        /// <param name="request">Session creation request</param>
        /// <returns>The ID of the created session</returns>
        /// <response code="200">Returns the session ID</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="429">If too many requests are made</response>
        [HttpPost("session")]
        [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> CreateKycSession([FromBody] CreateSessionRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "CreateKycSession",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    // Validate input
                    if (!ModelState.IsValid)
                    {
                        var errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        );

                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid request data",
                            "INVALID_REQUEST",
                            errors)
                            .ToActionResult(this);
                    }

                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Rate limiting check
                    var rateLimitKey = $"kyc_session_creation_{userId}";
                    var sessionCount = _cache.TryGetValue(rateLimitKey, out int count) ? count : 0;

                    if (sessionCount >= 5)
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Too many session creation attempts. Please try again later.",
                            "RATE_LIMIT_EXCEEDED")
                            .ToActionResult(this);
                    }

                    var result = await _kycSessionService.GetOrCreateUserSessionAsync(userId.Value);

                    if (!result.IsSuccess)
                    {
                        throw new Exception($"Failed to create KYC session: {result.ErrorMessage}");
                    }

                    // Update rate limit counter
                    _cache.Set(rateLimitKey, sessionCount + 1, TimeSpan.FromHours(1));

                    // Clear KYC status cache
                    _cache.Remove($"kyc_status_{userId}");

                    await LogSecurityEvent(userId.Value, "KYC_SESSION_CREATED", "KYC session created");

                    _logger.LogInformation("KYC session created successfully for user {UserId}", userId);

                    return ResultWrapper.Success(result.Data.Id, "KYC session created successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating KYC session");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Invalidate a KYC session
        /// </summary>
        /// <param name="request">Session invalidation request</param>
        /// <returns>Success confirmation</returns>
        [HttpDelete("session")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> InvalidateSession([FromBody] InvalidateSessionRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid or missing session ID",
                        "INVALID_REQUEST")
                        .ToActionResult(this);
                }

                var result = await _kycSessionService.InvalidateSessionAsync(sessionId, userId.Value, request?.Reason ?? "User requested");

                if (!result.IsSuccess)
                {
                    throw new Exception($"Failed to invalidate KYC session: {result.ErrorMessage}");
                }

                return ResultWrapper.Success(new { invalidated = true }, "Session invalidated successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating session");
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Validate a KYC session
        /// </summary>
        /// <returns>Session validation result</returns>
        [HttpGet("session/validate")]
        [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ValidateSession()
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid or missing session ID",
                        "INVALID_REQUEST")
                        .ToActionResult(this);
                }

                var validationResult = await _kycSessionService.ValidateSessionAsync(sessionId, userId.Value);

                if(!validationResult.IsSuccess)
                {
                    throw new Exception($"Failed to validate KYC session: {validationResult.ErrorMessage}");
                }

                var message = validationResult.IsSuccess ? "Session is valid" : "Session is invalid";
                return ResultWrapper.Success(validationResult.IsSuccess, message)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating session");
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Update session progress
        /// </summary>
        /// <param name="progress">Progress information</param>
        /// <returns>Update confirmation</returns>
        [HttpPatch("session/progress")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UpdateSessionProgress([FromBody] SessionProgress progress)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                        "Invalid or missing session ID",
                        "INVALID_REQUEST")
                        .ToActionResult(this);
                }

                var updateResult = await _kycSessionService.UpdateSessionProgressAsync(sessionId, progress);

                var message = updateResult.IsSuccess ? "Progress updated" : updateResult.ErrorMessage;
                return ResultWrapper.Success(new { updated = updateResult.IsSuccess }, message)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating session progress");
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Submit KYC verification data
        /// </summary>
        /// <param name="submission">KYC verification submission</param>
        /// <returns>Verification result</returns>
        /// <response code="200">Returns the verification result</response>
        /// <response code="400">If the submission is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpPost("verify")]
        [RequestSizeLimit(52428800)] // 50MB limit
        [ProducesResponseType(typeof(KycVerificationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> VerifyKyc([FromBody] KycVerificationSubmissionRequest submission)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "VerifyKyc",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    // Validate input
                    if (!ModelState.IsValid)
                    {
                        var errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        );

                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid submission data",
                            "INVALID_REQUEST",
                            errors)
                            .ToActionResult(this);
                    }

                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Enhanced security validation
                    var securityValidation = await ValidateSubmissionSecurity(submission, userId.Value);

                    if (!securityValidation.IsValid)
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            securityValidation.ErrorMessage,
                            "SECURITY_VALIDATION_ERROR")
                            .ToActionResult(this);
                    }

                    // Create verification request
                    var verificationRequest = new KycVerificationRequest
                    {
                        UserId = userId.Value,
                        SessionId = submission.SessionId,
                        VerificationLevel = submission.VerificationLevel,
                        Data = submission.Data,
                        ConsentGiven = submission.ConsentGiven,
                        TermsAccepted = submission.TermsAccepted
                    };

                    var result = await _kycService.VerifyAsync(verificationRequest);
                    if (!result.IsSuccess)
                    {
                        await LogSecurityEvent(userId.Value, "KYC_VERIFICATION_FAILED",
                            $"KYC verification failed: {result.ErrorMessage}");

                        return ResultWrapper.Failure(FailureReason.KycVerificationError,
                            "KYC verification failed.",
                            "VERIFICATION_FAILED")
                            .ToActionResult(this);
                    }

                    // Clear cache
                    _cache.Remove($"kyc_status_{userId}");

                    await LogSecurityEvent(userId.Value, "KYC_VERIFICATION_SUBMITTED",
                        $"KYC verification submitted with status: {result.Data.Status}");

                    var response = new KycVerificationResponse
                    {
                        Status = result.Data.Status,
                        VerificationLevel = result.Data.VerificationLevel,
                        SubmittedAt = result.Data.UpdatedAt,
                        NextSteps = GetNextSteps(result.Data.Status)
                    };

                    _logger.LogInformation("KYC verification submitted successfully for user {UserId}", userId);

                    return ResultWrapper.Success(response, "KYC verification submitted successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in KYC verification");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Upload document for KYC verification
        /// </summary>
        /// <param name="request">Document upload request</param>
        /// <returns>Upload result</returns>
        /// <response code="200">Returns the upload result</response>
        /// <response code="400">If the upload request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpPost("document/upload")]
        [RequestSizeLimit(10485760)] // 10MB limit
        [ProducesResponseType(typeof(DocumentUploadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "UploadDocument",
                ["DocumentType"] = request?.DocumentType,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Validate Session ID
                    if (!Guid.TryParse(request.SessionId, out var sessionId))
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid Session ID",
                            "SESSION_VALIDATION_ERROR")
                            .ToActionResult(this);
                    }

                    // Use DocumentService for upload
                    var uploadResult = await _documentService.UploadDocumentAsync(
                        userId.Value,
                        sessionId,
                        request.File,
                        request.DocumentType);

                    if (!uploadResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(uploadResult.Reason,
                            uploadResult.ErrorMessage,
                            "UPLOAD_FAILED")
                            .ToActionResult(this);
                    }

                    await LogSecurityEvent(userId.Value, "DOCUMENT_UPLOADED",
                        $"Document uploaded: {request.DocumentType}, Size: {request.File.Length} bytes");

                    _logger.LogInformation("Document uploaded successfully for user {UserId}, type {DocumentType}",
                        userId, request.DocumentType);

                    return ResultWrapper.Success(uploadResult.Data, "Document uploaded successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error uploading document");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Download document for review (Admin only)
        /// </summary>
        /// <param name="documentId">Document ID</param>
        /// <returns>Document file</returns>
        /// <response code="200">Returns the document file</response>
        /// <response code="404">If document is not found</response>
        /// <response code="403">If access is denied</response>
        [HttpGet("admin/document/{documentId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DownloadDocument(Guid documentId)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Use DocumentService for download
                var downloadResult = await _documentService.DownloadDocumentAsync(
                    documentId,
                    userId.Value,
                    isAdminRequest: true);

                if (!downloadResult.IsSuccess)
                {
                    ResultWrapper.Failure(downloadResult.Reason,
                            downloadResult.ErrorMessage,
                            "DOWNLOAD_FAILED").ToActionResult(this);
                }

                var fileData = downloadResult.Data.FileData;
                var contentType = downloadResult.Data.ContentType;
                var fileName = downloadResult.Data.FileName;

                return File(fileData, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading document {DocumentId}", documentId);
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Download live-capture for review (Admin only)
        /// </summary>
        /// <param name="captureId">Live capture ID</param>
        /// <returns>Zip file containing live capture files</returns>
        /// <response code="200">Returns the live capture files as zip</response>
        /// <response code="404">If live capture is not found</response>
        /// <response code="403">If access is denied</response>
        [HttpGet("admin/live-capture/{captureId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DownloadLiveCapture(Guid captureId)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Use DocumentService for download
                var downloadResult = await _liveCaptureService.DownloadLiveCaptureAsync(
                    captureId,
                    userId.Value,
                    isAdminRequest: true);

                if (!downloadResult.IsSuccess)
                {
                    return ResultWrapper.Failure(downloadResult.Reason,
                            downloadResult.ErrorMessage,
                            "DOWNLOAD_FAILED").ToActionResult(this);
                }

                var fileDatas = downloadResult.Data.FileDatas;
                var contentType = downloadResult.Data.ContentType;
                var fileNames = downloadResult.Data.FileNames;

                var zipFileName = $"LiveCaptures_{captureId}.zip";

                using var memoryStream = new MemoryStream();
                using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    for (int i = 0; i < fileDatas.Count; i++)
                    {
                        var entry = archive.CreateEntry(fileNames[i], System.IO.Compression.CompressionLevel.Fastest);
                        using var entryStream = entry.Open();
                        await entryStream.WriteAsync(fileDatas[i], 0, fileDatas[i].Length);
                    }
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return File(memoryStream.ToArray(), "application/zip", zipFileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading live capture {CaptureId}", captureId);
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Get KYC verification requirements
        /// </summary>
        /// <param name="level">Verification level</param>
        /// <returns>KYC requirements for the specified level</returns>
        /// <response code="200">Returns the KYC requirements</response>
        [HttpGet("requirements")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(KycRequirementsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetKycRequirements([FromQuery] string level = "STANDARD")
        {
            try
            {
                var requirements = new KycRequirementsResponse
                {
                    VerificationLevel = level,
                    RequiredDocuments = GetRequiredDocuments(level),
                    SupportedFileTypes = _allowedFileTypes,
                    MaxFileSize = _maxFileSize,
                    ProcessingTime = GetEstimatedProcessingTime(level),
                    SecurityFeatures = new List<string>
                    {
                        "256-bit AES encryption",
                        "Document tamper detection",
                        "Biometric liveness verification",
                        "Advanced fraud detection"
                    }
                };

                return ResultWrapper.Success(requirements, "KYC requirements retrieved successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving KYC requirements");
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Check if user meets KYC requirements for a specific level
        /// </summary>
        /// <param name="requiredLevel">Required verification level</param>
        /// <returns>User's KYC eligibility status</returns>
        /// <response code="200">Returns the eligibility status</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpGet("eligibility")]
        [ProducesResponseType(typeof(KycEligibilityResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> CheckKycEligibility([FromQuery] string requiredLevel = "STANDARD")
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var isVerified = await _kycService.IsUserVerifiedAsync(userId.Value, requiredLevel);
                var isTradingEligible = await _kycService.IsUserEligibleForTrading(userId.Value);

                var eligibilityResponse = new KycEligibilityResponse
                {
                    IsVerified = isVerified.IsSuccess && isVerified.Data,
                    IsTradingEligible = isTradingEligible.IsSuccess && isTradingEligible.Data,
                    RequiredLevel = requiredLevel,
                    CheckedAt = DateTime.UtcNow
                };

                return ResultWrapper.Success(eligibilityResponse, "KYC eligibility checked successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking KYC eligibility");
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        // Admin endpoints
        /// <summary>
        /// Get pending KYC verifications (Admin only)
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>Paginated list of pending verifications</returns>
        /// <response code="200">Returns the pending verifications</response>
        /// <response code="403">If user is not an admin</response>
        [HttpGet("admin/pending")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(typeof(PaginatedResult<KycAdminView>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPendingVerifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "GetPendingVerifications",
                ["Page"] = page,
                ["PageSize"] = pageSize,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    var result = await _kycService.GetPendingVerificationsAsync(page, pageSize);
                    if (!result.IsSuccess)
                    {
                        return ResultWrapper.Failure(result.Reason,
                            result.ErrorMessage,
                            "FETCH_ERROR")
                            .ToActionResult(this);
                    }

                    var adminViews = result.Data.Items.Select(MapToAdminView).ToList();
                    var adminResult = new PaginatedResult<KycAdminView>
                    {
                        Items = adminViews,
                        Page = result.Data.Page,
                        PageSize = result.Data.PageSize,
                        TotalCount = result.Data.TotalCount,
                    };

                    _logger.LogInformation("Retrieved {Count} pending KYC verifications", adminViews.Count);

                    return ResultWrapper.Success(adminResult, "Pending verifications retrieved successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error retrieving pending verifications");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Update KYC status (Admin only)
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="request">Status update request</param>
        /// <returns>Update result</returns>
        /// <response code="200">Returns the update result</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If user is not authenticated as admin</response>
        /// <response code="404">If user KYC record is not found</response>
        [HttpPut("admin/status/{userId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateKycStatus(
            Guid userId,
            [FromBody] UpdateKycStatusRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "UpdateKycStatus",
                ["TargetUserId"] = userId,
                ["NewStatus"] = request?.Status,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    // Validate input
                    if (!ModelState.IsValid)
                    {
                        var errors = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        );

                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid request data",
                            "INVALID_REQUEST",
                            errors)
                            .ToActionResult(this);
                    }

                    var adminUserId = GetUserId();

                    // Validate the new status
                    if (!KycStatus.AllValues.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid KYC status provided",
                            "INVALID_STATUS")
                            .ToActionResult(this);
                    }

                    // Check if user exists and get current status
                    var currentKycResult = await _kycService.GetUserKycDataDecryptedAsync(userId);
                    if (!currentKycResult.IsSuccess)
                    {
                        return ResultWrapper.NotFound("User KYC record", userId.ToString())
                            .ToActionResult(this);
                    }

                    // Update KYC status
                    var updateResult = await _kycService.UpdateKycStatusAsync(
                        userId,
                        request.Status,
                        adminUserId.Value.ToString(),
                        request.Reason);

                    if (!updateResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(FailureReason.Unknown,
                            updateResult.ErrorMessage,
                            "UPDATE_FAILED")
                            .ToActionResult(this);
                    }

                    // Clear user's KYC status cache
                    _cache.Remove($"kyc_status_{userId}");

                    // Log security event
                    await LogSecurityEvent(adminUserId.Value, "KYC_STATUS_UPDATED",
                        $"Admin updated KYC status for user {userId} from {currentKycResult.Data.Status} to {request.Status}. Reason: {request.Reason ?? "No reason provided"}");

                    var responseData = new
                    {
                        userId = userId,
                        previousStatus = currentKycResult.Data.Status,
                        newStatus = request.Status,
                        updatedBy = adminUserId.Value,
                        updatedAt = DateTime.UtcNow,
                        reason = request.Reason
                    };

                    _logger.LogInformation("KYC status updated successfully for user {UserId} by admin {AdminId}",
                        userId, adminUserId);

                    return ResultWrapper.Success(responseData, "KYC status updated successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating KYC status for user {UserId}", userId);
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Submit live capture data for KYC verification
        /// </summary>
        /// <param name="request">Live document capture request</param>
        /// <returns>Live capture processing result</returns>
        /// <response code="200">Returns the processing result</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpPost("document/live-capture")]
        [RequestSizeLimit(20971520)] // 20MB limit for high-quality images
        [ProducesResponseType(typeof(LiveCaptureResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ProcessLiveDocumentCapture([FromBody] LiveDocumentCaptureRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "ProcessLiveDocumentCapture",
                ["DocumentType"] = request?.DocumentType,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Use DocumentService for live capture processing
                    var processingResult = await _liveCaptureService.ProcessLiveDocumentCaptureAsync(userId.Value, request);
                    if (!processingResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(processingResult.Reason,
                            processingResult.ErrorMessage,
                            "INVALID_REQUEST")
                            .ToActionResult(this);
                    }

                    await LogSecurityEvent(userId.Value, "LIVE_DOCUMENT_CAPTURED",
                        $"Live document captured: {request.DocumentType}, Quality: {request.ImageData.Sum(s => s.QualityScore) / request.ImageData.Count()}%, Live: {request.IsLive}");

                    _logger.LogInformation("Live document capture processed successfully for user {UserId}", userId);

                    return ResultWrapper.Success(processingResult.Data, "Live capture processed successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing live capture");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Submit live selfie capture data for KYC verification
        /// </summary>
        /// <param name="request">Live selfie capture request</param>
        /// <returns>Live capture processing result</returns>
        /// <response code="200">Returns the processing result</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        [HttpPost("selfie/live-capture")]
        [RequestSizeLimit(20971520)] // 20MB limit for high-quality images
        [ProducesResponseType(typeof(LiveCaptureResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ProcessLiveSelfieCapture([FromBody] LiveSelfieCaptureRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "ProcessLiveSelfieCapture",
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    var userId = GetUserId();
                    if (!userId.HasValue)
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Use DocumentService for live capture processing
                    var processingResult = await _liveCaptureService.ProcessLiveSelfieCaptureAsync(userId.Value, request);
                    if (!processingResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(processingResult.Reason,
                            processingResult.ErrorMessage,
                            "INVALID_REQUEST")
                            .ToActionResult(this);
                    }

                    await LogSecurityEvent(userId.Value, "LIVE_SELFIE_CAPTURED",
                        $"Live selfie captured, Live: {request.IsLive}");

                    _logger.LogInformation("Live selfie capture processed successfully for user {UserId}", userId);

                    return ResultWrapper.Success(processingResult.Data, "Live capture processed successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing live capture");
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        #region Private helper methods
        private Guid? GetUserId()
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        private async Task<ValidationResult> ValidateSubmissionSecurity(KycVerificationSubmissionRequest submission, Guid userId)
        {
            var errors = new List<string>();

            // Validate session ownership
            if (submission.SessionId == Guid.Empty)
            {
                errors.Add("Invalid session ID");
            }

            // Validate data integrity
            if (submission.Data == null)
            {
                errors.Add("Verification data is required");
            }

            // TO-DO: Validate submission data based on verification level

            if (submission.VerificationLevel == VerificationLevel.Basic)
            {
                var basicKycData = BasicKycDataRequest.FromDictionary(submission.Data);
                if (basicKycData.PersonalInfo != null)
                {
                    var personalInfo = basicKycData.PersonalInfo;
                    if (personalInfo == null)
                    {
                        var personalDataErrors = ValidatePersonalData(personalInfo);
                        errors.AddRange(personalDataErrors);
                    }
                }
            }

            if (submission.VerificationLevel == VerificationLevel.Standard)
            {
                var standardKycData = StandardKycDataRequest.FromDictionary(submission.Data);
                if (standardKycData.PersonalInfo != null)
                {
                    var personalInfo = standardKycData.PersonalInfo;
                    if (personalInfo == null)
                    {
                        var personalDataErrors = ValidatePersonalData(personalInfo);
                        errors.AddRange(personalDataErrors);
                    }
                }
            }

            // Check for suspicious patterns
            var suspiciousPatterns = new[]
            {
                @"<script",
                @"javascript:",
                @"data:text/html",
                @"vbscript:",
                @"onload=",
                @"onerror="
            };

            var submissionJson = JsonConvert.SerializeObject(submission.Data);
            foreach (var pattern in suspiciousPatterns)
            {
                if (Regex.IsMatch(submissionJson, pattern, RegexOptions.IgnoreCase))
                {
                    errors.Add("Suspicious content detected");
                    await LogSecurityEvent(userId, "SUSPICIOUS_CONTENT_DETECTED",
                        $"Suspicious pattern detected in submission: {pattern}");
                    break;
                }
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                ErrorMessage = string.Join("; ", errors)
            };
        }

        private List<string> ValidatePersonalData(object personalInfo)
        {
            var errors = new List<string>();
            if (personalInfo is BasicPersonalInfoRequest basicInfo)
            {
                // Validate full name
                if (!IsValidName(basicInfo.FullName))
                    errors.Add("Invalid full name format");
                // Validate date of birth
                if (!DateTime.TryParse(basicInfo.DateOfBirth, out var dob) || !IsValidDateOfBirth(dob))
                    errors.Add("Invalid date of birth format");
                // Validate address
                if (basicInfo.Address == null)
                {
                    errors.Add("Address is required");
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(basicInfo.Address.Street) ||
                        string.IsNullOrWhiteSpace(basicInfo.Address.City) ||
                        string.IsNullOrWhiteSpace(basicInfo.Address.State) ||
                        string.IsNullOrWhiteSpace(basicInfo.Address.ZipCode) ||
                        string.IsNullOrWhiteSpace(basicInfo.Address.Country))
                    {
                        errors.Add("Complete address is required");
                    }
                }
            }
            else if (personalInfo is StandardPersonalInfoRequest standardInfo)
            {
                // Validate document number
                if (!IsValidDocumentNumber(standardInfo.GovernmentIdNumber))
                    errors.Add("Invalid government Id number format");

                // Validate phone number
                if (!IsValidPhoneNumber(standardInfo.PhoneNumber))
                    errors.Add("Invalid phone number format");

                if (string.IsNullOrWhiteSpace(standardInfo.Nationality))
                {
                    errors.Add("Nationality is required");
                }

                if (string.IsNullOrWhiteSpace(standardInfo.Occupation))
                {
                    errors.Add("Occupation is required");
                }
            }
            return errors;
        }

        private bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return Regex.IsMatch(name, @"^[a-zA-Z\s\-']+$") && name.Length >= 2 && name.Length <= 50;
        }

        private bool IsValidDateOfBirth(DateTime dateOfBirth)
        {
            var age = DateTime.UtcNow.Year - dateOfBirth.Year;
            if (dateOfBirth > DateTime.UtcNow.AddYears(-age)) age--;

            return age >= 18 && age <= 120;
        }

        private bool IsValidDocumentNumber(string? docNumber)
        {
            if (string.IsNullOrWhiteSpace(docNumber))
                return false;

            return Regex.IsMatch(docNumber, @"^[a-zA-Z0-9]+$") && docNumber.Length >= 5 && docNumber.Length <= 20;
        }

        private bool IsValidPhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return false;

            return Regex.IsMatch(phoneNumber, @"^\+?[\d\s\-()]+$") && phoneNumber.Length >= 10 && phoneNumber.Length <= 20;
        }

        private KycStatusResponse MapToStatusResponse(KycStatusDto statusDto)
        {
            return new KycStatusResponse
            {
                Status = statusDto.Status,
                VerificationLevel = statusDto.VerificationLevel,
                SubmittedAt = statusDto.SubmittedAt,
                UpdatedAt = statusDto.UpdatedAt,
                VerifiedAt = statusDto.VerifiedAt,
                ExpiresAt = statusDto.ExpiresAt,
                NextSteps = GetNextSteps(statusDto.Status)
            };
        }

        private KycAdminView MapToAdminView(KycData kycData)
        {
            return new KycAdminView
            {
                Id = kycData.Id,
                UserId = kycData.UserId,
                Status = kycData.Status,
                VerificationLevel = kycData.VerificationLevel,
                SubmittedAt = kycData.CreatedAt,
                UpdatedAt = kycData.UpdatedAt,
                RequiresReview = kycData.SecurityFlags?.RequiresReview ?? false,
                RiskLevel = DetermineRiskLevel(kycData),
                PersonalInfo = kycData.PersonalData,
                LiveCaptures = MapToLiveCaptureDtos(kycData.LiveCaptures).Result,
                Documents = MapToDocumentDtos(kycData.Documents).Result,
                History = kycData.History,
            };
        }

        private async Task<List<LiveCaptureDto>> MapToLiveCaptureDtos(List<Guid> liveCaptureIds)
        {
            var liveCaptures = new List<LiveCaptureDto>();
            foreach (var id in liveCaptureIds)
            {
                var capture = await _liveCaptureService.GetLiveCaptureAsync(id);
                if (capture.IsSuccess)
                {
                    liveCaptures.Add(new LiveCaptureDto
                    {
                        Id = capture.Data.Id,
                        Type = capture.Data.DocumentType,
                        FileSize = capture.Data.FileSize,
                        UploadedAt = capture.Data.CreatedAt,
                        Status = capture.Data.Status,
                    });
                }
            }
            return liveCaptures;
        }

        private async Task<List<DocumentDto>> MapToDocumentDtos(List<Guid> documentIds)
        {
            var documents = new List<DocumentDto>();
            foreach (var id in documentIds)
            {
                var capture = await _documentService.GetDocumentAsync(id);
                if (capture.IsSuccess)
                {
                    documents.Add(new DocumentDto
                    {
                        Id = capture.Data.Id,
                        Type = capture.Data.DocumentType,
                        FileName = capture.Data.OriginalFileName,
                        FileSize = capture.Data.FileSize,
                        UploadedAt = capture.Data.CreatedAt,
                        Status = capture.Data.Status,
                    });
                }
            }
            return documents;
        }

        private List<string> GetNextSteps(string status)
        {
            return status switch
            {
                KycStatus.NotStarted => new List<string> { "Complete personal information", "Upload identity documents", "Take verification photo" },
                KycStatus.Pending => new List<string> { "Wait for verification review", "Check email for updates" },
                KycStatus.NeedsReview => new List<string> { "Wait for manual review", "Respond to any additional requests" },
                KycStatus.Approved => new List<string> { "Verification complete", "All features unlocked" },
                KycStatus.Rejected => new List<string> { "Contact support", "Review rejection reasons", "Resubmit if eligible" },
                _ => new List<string> { "Contact support for assistance" }
            };
        }

        private List<string> GetRequiredDocuments(string level)
        {
            return level switch
            {
                KycLevel.Basic => new List<string> { "Government-issued ID" },
                KycLevel.Standard => new List<string> { "Government-issued ID", "Proof of address", "Selfie photo" },
                KycLevel.Advanced => new List<string> { "Government-issued ID", "Proof of address", "Selfie photo", "Income verification" },
                _ => new List<string> { "Government-issued ID" }
            };
        }

        private string GetEstimatedProcessingTime(string level)
        {
            return level switch
            {
                KycLevel.Basic => "1-2 business days",
                KycLevel.Standard => "2-3 business days",
                KycLevel.Advanced => "3-5 business days",
                _ => "1-2 business days"
            };
        }

        private string DetermineRiskLevel(KycData kycData)
        {
            if (kycData.SecurityFlags?.RequiresReview == true)
                return "HIGH";

            if (kycData.History.Count > 3)
                return "MEDIUM";

            return "LOW";
        }

        private async Task LogSecurityEvent(Guid userId, string eventType, string details)
        {
            var securityEvent = new SecurityEvent
            {
                UserId = userId,
                EventType = eventType,
                Details = details,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext.Request.Headers["User-Agent"].ToString(),
                Timestamp = DateTime.UtcNow
            };

            _logger.LogInformation("Security Event: {EventType} - User: {UserId} - {Details}", eventType, userId, details);

            // Additional security event logging would go here
        }
        #endregion
    }

    public class KycVerificationResponse
    {
        public string Status { get; set; } = string.Empty;
        public string VerificationLevel { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public List<string> NextSteps { get; set; } = new();
    }

    public class KycRequirementsResponse
    {
        public string VerificationLevel { get; set; } = string.Empty;
        public List<string> RequiredDocuments { get; set; } = new();
        public string[] SupportedFileTypes { get; set; } = Array.Empty<string>();
        public long MaxFileSize { get; set; }
        public string ProcessingTime { get; set; } = string.Empty;
        public List<string> SecurityFeatures { get; set; } = new();
    }

    public class KycEligibilityResponse
    {
        public bool IsVerified { get; set; }
        public bool IsTradingEligible { get; set; }
        public string RequiredLevel { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
    }

    public class KycAdminView
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string VerificationLevel { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool RequiresReview { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public Dictionary<string, object>? PersonalInfo { get; set; }
        public List<LiveCaptureDto> LiveCaptures { get; set; } = new();
        public List<DocumentDto> Documents { get; set; } = new();
        public List<KycHistoryEntry> History { get; set; } = new();
    }

    public class SecurityEvent
    {
        public Guid UserId { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}