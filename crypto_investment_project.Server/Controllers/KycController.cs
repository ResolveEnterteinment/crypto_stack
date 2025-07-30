// Fixed Production-Ready KYC Controller with Security Features
// Controllers/KycController.cs

using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Claims;
using System.Text.Json;
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
        private readonly IDocumentService _documentService;
        private readonly ILoggingService _logger;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly IAntiforgery _antiforgery;
        private readonly IWebHostEnvironment _environment;
        private readonly string[] _allowedFileTypes = { ".jpg", ".jpeg", ".png", ".pdf", ".webp" };
        private readonly long _maxFileSize = 10 * 1024 * 1024; // 10MB

        public KycController(
            IKycService kycService,
            ILoggingService logger,
            IMemoryCache cache,
            IConfiguration configuration,
            IAntiforgery antiforgery,
            IWebHostEnvironment environment,
            IDocumentService documentService)
        {
            _kycService = kycService ?? throw new ArgumentNullException(nameof(kycService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _antiforgery = antiforgery ?? throw new ArgumentNullException(nameof(antiforgery));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        }

        /// <summary>
        /// Get current KYC status for the authenticated user
        /// </summary>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResponse<KycStatusResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetKycStatus()
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Check cache first
                var cacheKey = $"kyc_status_{userId}";
                if (_cache.TryGetValue(cacheKey, out KycStatusResponse? cachedStatus) && cachedStatus != null)
                {
                    return Ok(new ApiResponse<KycStatusResponse>
                    {
                        Success = true,
                        Data = cachedStatus,
                        Message = "KYC status retrieved from cache"
                    });
                }

                var result = await _kycService.GetUserKycStatusAsync(userId.Value);
                if (!result.IsSuccess)
                {
                    return StatusCode(500, new ApiErrorResponse
                    {
                        Error = "KYC_FETCH_ERROR",
                        Message = result.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var statusResponse = MapToStatusResponse(result.Data);

                // Cache for 5 minutes
                _cache.Set(cacheKey, statusResponse, TimeSpan.FromMinutes(5));

                return Ok(new ApiResponse<KycStatusResponse>
                {
                    Success = true,
                    Data = statusResponse,
                    Message = "KYC status retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving KYC status: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while retrieving KYC status",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Create a new KYC verification session
        /// </summary>
        [HttpPost("session")]
        [ProducesResponseType(typeof(ApiResponse<Guid>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> CreateKycSession([FromBody] CreateSessionRequest request)
        {
            try
            {
                // Validate input
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "VALIDATION_ERROR",
                        Message = "Invalid request data",
                        Details = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        ),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Rate limiting check
                var rateLimitKey = $"kyc_session_creation_{userId}";
                var sessionCount = _cache.TryGetValue(rateLimitKey, out int count) ? count : 0;

                if (sessionCount >= 5)
                {
                    return StatusCode(429, new ApiErrorResponse
                    {
                        Error = "RATE_LIMIT_EXCEEDED",
                        Message = "Too many session creation attempts. Please try again later.",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Validate verification level
                if (!IsValidVerificationLevel(request.VerificationLevel))
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "INVALID_VERIFICATION_LEVEL",
                        Message = "Invalid verification level specified",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var result = await _kycService.GetOrCreateUserSessionAsync(userId.Value, request.VerificationLevel);
                if (!result.IsSuccess)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "SESSION_CREATION_ERROR",
                        Message = result.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Update rate limit counter
                _cache.Set(rateLimitKey, sessionCount + 1, TimeSpan.FromHours(1));

                // Clear KYC status cache
                _cache.Remove($"kyc_status_{userId}");

                await LogSecurityEvent(userId.Value, "KYC_SESSION_CREATED",
                    $"KYC session created for verification level: {request.VerificationLevel}");

                return Ok(new ApiResponse<Guid>
                {
                    Success = true,
                    Data = result.Data.Id,
                    Message = "KYC session created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating KYC session: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while creating KYC session",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        [HttpDelete("session")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> InvalidateSession([FromBody] InvalidateSessionRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized();
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return BadRequest();
                }

                await _kycService.InvalidateSessionAsync(sessionId, userId.Value, request?.Reason ?? "User requested");

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new { invalidated = true },
                    Message = "Session invalidated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error invalidating session: {ex.Message}");
                return StatusCode(500);
            }
        }

        [HttpGet("session/validate")]
        [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ValidateSession()
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "INVALID_SESSION",
                        Message = "Invalid or missing session ID",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var validationResult = await _kycService.ValidateSessionAsync(sessionId, userId.Value);

                return Ok(new ApiResponse<bool>
                {
                    Success = validationResult.IsSuccess,
                    Data = validationResult.IsSuccess,
                    Message = validationResult.IsSuccess ? "Session is valid" : "Session is invalid"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error validating session: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while validating session",
                    TraceId = HttpContext.TraceIdentifier
                });
            }

        }

        [HttpPatch("session/progress")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateSessionProgress([FromBody] SessionProgress progress)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized();
                }

                var sessionHeader = HttpContext.Request.Headers["X-KYC-Session"].FirstOrDefault();
                if (string.IsNullOrEmpty(sessionHeader) || !Guid.TryParse(sessionHeader, out var sessionId))
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "INVALID_SESSION",
                        Message = "Invalid or missing session ID"
                    });
                }

                var updateResult = await _kycService.UpdateSessionProgressAsync(sessionId, progress);

                return Ok(new ApiResponse<object>
                {
                    Success = updateResult.IsSuccess,
                    Data = new { updated = updateResult.IsSuccess },
                    Message = updateResult.IsSuccess ? "Progress updated" : updateResult.ErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating session progress: {ex.Message}");
                return StatusCode(500);
            }
        }

        /// <summary>
        /// Submit KYC verification data
        /// </summary>
        [HttpPost("verify")]
        [RequestSizeLimit(52428800)] // 50MB limit
        [ProducesResponseType(typeof(ApiResponse<KycVerificationResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> VerifyKyc([FromBody] KycVerificationSubmissionRequest submission)
        {
            try
            {
                // Validate input
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "VALIDATION_ERROR",
                        Message = "Invalid submission data",
                        Details = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        ),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Enhanced security validation
                var securityValidation = await ValidateSubmissionSecurity(submission, userId.Value);
                if (!securityValidation.IsValid)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "SECURITY_VALIDATION_FAILED",
                        Message = securityValidation.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
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

                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "VERIFICATION_FAILED",
                        Message = result.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Clear cache
                _cache.Remove($"kyc_status_{userId}");

                await LogSecurityEvent(userId.Value, "KYC_VERIFICATION_SUBMITTED",
                    $"KYC verification submitted with status: {result.Data.Status}");

                return Ok(new ApiResponse<KycVerificationResponse>
                {
                    Success = true,
                    Data = new KycVerificationResponse
                    {
                        Status = result.Data.Status,
                        VerificationLevel = result.Data.VerificationLevel,
                        SubmittedAt = result.Data.UpdatedAt ?? DateTime.UtcNow,
                        NextSteps = GetNextSteps(result.Data.Status)
                    },
                    Message = "KYC verification submitted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in KYC verification: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred during KYC verification",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Upload document for KYC verification
        /// </summary>
        [HttpPost("document/upload")]
        [RequestSizeLimit(10485760)] // 10MB limit
        [ProducesResponseType(typeof(ApiResponse<DocumentUploadResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Validate Session ID
                if (!Guid.TryParse(request.SessionId, out var sessionId))
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "SESSION_VALIDATION_ERROR",
                        Message = "Invalid Session ID",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Use DocumentService for upload
                var uploadResult = await _documentService.UploadDocumentAsync(
                    userId.Value,
                    sessionId,
                    request.File,
                    request.DocumentType);

                if (!uploadResult.IsSuccess)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "UPLOAD_FAILED",
                        Message = uploadResult.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                await LogSecurityEvent(userId.Value, "DOCUMENT_UPLOADED",
                    $"Document uploaded: {request.DocumentType}, Size: {request.File.Length} bytes");

                return Ok(new ApiResponse<DocumentUploadResponse>
                {
                    Success = true,
                    Data = uploadResult.Data,
                    Message = "Document uploaded successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error uploading document: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "UPLOAD_ERROR",
                    Message = "An error occurred during document upload",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Download document for review (Admin only)
        /// </summary>
        [HttpGet("admin/document/{documentId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DownloadDocument(Guid documentId)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Use DocumentService for download
                var downloadResult = await _documentService.DownloadDocumentAsync(
                    documentId,
                    userId.Value,
                    isAdminRequest: true);

                if (!downloadResult.IsSuccess)
                {
                    return downloadResult.Reason switch
                    {
                        FailureReason.NotFound => NotFound(new ApiErrorResponse
                        {
                            Error = "DOCUMENT_NOT_FOUND",
                            Message = "Document not found or access denied",
                            TraceId = HttpContext.TraceIdentifier
                        }),
                        FailureReason.SecurityError => StatusCode(422, new ApiErrorResponse
                        {
                            Error = "SECURITY_ERROR",
                            Message = downloadResult.ErrorMessage,
                            TraceId = HttpContext.TraceIdentifier
                        }),
                        _ => StatusCode(500, new ApiErrorResponse
                        {
                            Error = "DOWNLOAD_ERROR",
                            Message = downloadResult.ErrorMessage,
                            TraceId = HttpContext.TraceIdentifier
                        })
                    };
                }

                var (fileData, contentType, fileName) = downloadResult.Data;
                return File(fileData, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error downloading document: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "DOWNLOAD_ERROR",
                    Message = "An error occurred during document download",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Download live-capture for review (Admin only)
        /// </summary>
        [HttpGet("admin/live-capture/{captureId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> DownloadLiveCapture(Guid captureId)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Use DocumentService for download
                var downloadResult = await _documentService.DownloadLiveCaptureAsync(
                    captureId,
                    userId.Value,
                    isAdminRequest: true);

                if (!downloadResult.IsSuccess)
                {
                    return downloadResult.Reason switch
                    {
                        FailureReason.NotFound => NotFound(new ApiErrorResponse
                        {
                            Error = "DOCUMENT_NOT_FOUND",
                            Message = "Document not found or access denied",
                            TraceId = HttpContext.TraceIdentifier
                        }),
                        FailureReason.SecurityError => StatusCode(422, new ApiErrorResponse
                        {
                            Error = "SECURITY_ERROR",
                            Message = downloadResult.ErrorMessage,
                            TraceId = HttpContext.TraceIdentifier
                        }),
                        _ => StatusCode(500, new ApiErrorResponse
                        {
                            Error = "DOWNLOAD_ERROR",
                            Message = downloadResult.ErrorMessage,
                            TraceId = HttpContext.TraceIdentifier
                        })
                    };
                }

                var (fileDatas, contentType, fileNames) = downloadResult.Data;
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
                _logger.LogError($"Error downloading live capture: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "DOWNLOAD_ERROR",
                    Message = "An error occurred during live capture download",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Get KYC verification requirements
        /// </summary>
        [HttpGet("requirements")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<KycRequirementsResponse>), StatusCodes.Status200OK)]
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

                return Ok(new ApiResponse<KycRequirementsResponse>
                {
                    Success = true,
                    Data = requirements,
                    Message = "KYC requirements retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving KYC requirements: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while retrieving requirements",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Check if user meets KYC requirements for a specific level
        /// </summary>
        [HttpGet("eligibility")]
        [ProducesResponseType(typeof(ApiResponse<KycEligibilityResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> CheckKycEligibility([FromQuery] string requiredLevel = "STANDARD")
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
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

                return Ok(new ApiResponse<KycEligibilityResponse>
                {
                    Success = true,
                    Data = eligibilityResponse,
                    Message = "KYC eligibility checked successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking KYC eligibility: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while checking eligibility",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        // Admin endpoints
        /// <summary>
        /// Get pending KYC verifications (Admin only)
        /// </summary>
        [HttpGet("admin/pending")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<PaginatedResult<KycAdminView>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetPendingVerifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var result = await _kycService.GetPendingVerificationsAsync(page, pageSize);
                if (!result.IsSuccess)
                {
                    return StatusCode(500, new ApiErrorResponse
                    {
                        Error = "FETCH_ERROR",
                        Message = result.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var adminViews = result.Data.Items.Select(MapToAdminView).ToList();
                var adminResult = new PaginatedResult<KycAdminView>
                {
                    Items = adminViews,
                    Page = result.Data.Page,
                    PageSize = result.Data.PageSize,
                    TotalCount = result.Data.TotalCount,
                    TotalPages = result.Data.TotalPages,
                    HasPreviousPage = result.Data.HasPreviousPage,
                    HasNextPage = result.Data.HasNextPage
                };

                return Ok(new ApiResponse<PaginatedResult<KycAdminView>>
                {
                    Success = true,
                    Data = adminResult,
                    Message = "Pending verifications retrieved successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving pending verifications: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while retrieving pending verifications",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Update KYC status (Admin only)
        /// </summary>
        [HttpPut("admin/status/{userId}")]
        [Authorize(Roles = "ADMIN")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateKycStatus(
            Guid userId,
            [FromBody] UpdateKycStatusRequest request)
        {
            try
            {
                // Validate input
                if (!ModelState.IsValid)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "VALIDATION_ERROR",
                        Message = "Invalid request data",
                        Details = ModelState.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? Array.Empty<string>()
                        ),
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                var adminUserId = GetUserId();
                if (!User.IsInRole("ADMIN"))
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid admin authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Validate the new status
                if (!KycStatus.AllValues.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "INVALID_STATUS",
                        Message = "Invalid KYC status provided",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Check if user exists and get current status
                var currentKycResult = await _kycService.GetUserKycDataDecryptedAsync(userId);
                if (!currentKycResult.IsSuccess)
                {
                    return NotFound(new ApiErrorResponse
                    {
                        Error = "USER_NOT_FOUND",
                        Message = "User KYC record not found",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Update KYC status
                var updateResult = await _kycService.UpdateKycStatusAsync(
                    userId,
                    request.Status,
                    adminUserId.Value.ToString(),
                    request.Reason);

                if (!updateResult.IsSuccess)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "UPDATE_FAILED",
                        Message = updateResult.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Clear user's KYC status cache
                _cache.Remove($"kyc_status_{userId}");

                // Log security event
                await LogSecurityEvent(adminUserId.Value, "KYC_STATUS_UPDATED",
                    $"Admin updated KYC status for user {userId} from {currentKycResult.Data.Status} to {request.Status}. Reason: {request.Reason ?? "No reason provided"}");

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Data = new
                    {
                        userId = userId,
                        previousStatus = currentKycResult.Data.Status,
                        newStatus = request.Status,
                        updatedBy = adminUserId.Value,
                        updatedAt = DateTime.UtcNow,
                        reason = request.Reason
                    },
                    Message = "KYC status updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating KYC status for user {userId}: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "INTERNAL_ERROR",
                    Message = "An error occurred while updating KYC status",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Submit live capture data for KYC verification
        /// </summary>
        [HttpPost("document/live-capture")]
        [RequestSizeLimit(20971520)] // 20MB limit for high-quality images
        [ProducesResponseType(typeof(ApiResponse<LiveCaptureResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ProcessLiveDocumentCapture([FromBody] LiveDocumentCaptureRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Use DocumentService for live capture processing
                var processingResult = await _documentService.ProcessLiveDocumentCaptureAsync(userId.Value, request);
                if (!processingResult.IsSuccess)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "LIVE_CAPTURE_PROCESSING_FAILED",
                        Message = processingResult.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                await LogSecurityEvent(userId.Value, "LIVE_DOCUMENT_CAPTURED",
                    $"Live document captured: {request.DocumentType}, Quality: {request.ImageData.Sum(s => s.QualityScore)/ request.ImageData.Count()}%, Live: {request.IsLive}");

                return Ok(new ApiResponse<LiveCaptureResponse>
                {
                    Success = true,
                    Data = processingResult.Data,
                    Message = "Live capture processed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing live capture: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "LIVE_CAPTURE_ERROR",
                    Message = "An error occurred during live capture processing",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }

        /// <summary>
        /// Submit live capture data for KYC verification
        /// </summary>
        [HttpPost("selfie/live-capture")]
        [RequestSizeLimit(20971520)] // 20MB limit for high-quality images
        [ProducesResponseType(typeof(ApiResponse<LiveCaptureResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> ProcessLiveSelfieCapture([FromBody] LiveSelfieCaptureRequest request)
        {
            try
            {
                var userId = GetUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(new ApiErrorResponse
                    {
                        Error = "UNAUTHORIZED",
                        Message = "Invalid user authentication",
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                // Use DocumentService for live capture processing
                var processingResult = await _documentService.ProcessLiveSelfieCaptureAsync(userId.Value, request);
                if (!processingResult.IsSuccess)
                {
                    return BadRequest(new ApiErrorResponse
                    {
                        Error = "LIVE_CAPTURE_PROCESSING_FAILED",
                        Message = processingResult.ErrorMessage,
                        TraceId = HttpContext.TraceIdentifier
                    });
                }

                await LogSecurityEvent(userId.Value, "LIVE_SELFIE_CAPTURED",
                    $"Live selfie captured, Live: {request.IsLive}");

                return Ok(new ApiResponse<LiveCaptureResponse>
                {
                    Success = true,
                    Data = processingResult.Data,
                    Message = "Live capture processed successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing live capture: {ex.Message}");
                return StatusCode(500, new ApiErrorResponse
                {
                    Error = "LIVE_CAPTURE_ERROR",
                    Message = "An error occurred during live capture processing",
                    TraceId = HttpContext.TraceIdentifier
                });
            }
        }
        // Private helper methods
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

        private bool IsValidVerificationLevel(string verificationLevel)
        {
            // Check if the provided verification level is valid
            return VerificationLevel.AllValues.Contains(verificationLevel, StringComparer.OrdinalIgnoreCase);
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
                var capture = await _documentService.GetLiveCaptureAsync(id);
                if (capture.IsSuccess)
                {
                    liveCaptures.Add(new LiveCaptureDto {
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

            _logger.LogInformation($"Security Event: {eventType} - User: {userId} - {details}");

            // Additional security event logging would go here
        }
    }

    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; } = default!;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class ApiErrorResponse
    {
        public string Error { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public Dictionary<string, string[]>? Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
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
        public List<LiveCaptureDto> LiveCaptures { get; set; }
        public List<DocumentDto> Documents { get; set; }
        public List<KycHistoryEntry> History { get; set; }
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

    // Required interface for PaginatedResult
    public class PaginatedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }
}