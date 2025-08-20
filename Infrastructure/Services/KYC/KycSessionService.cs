using Application.Interfaces.KYC;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models.KYC;
using Infrastructure.Services.Base;
using Infrastructure.Services.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.Security.Cryptography;

namespace Infrastructure.Services.KYC
{
    public class KycSessionService : BaseService<KycSessionData>, IKycSessionService
    {
        private readonly IKycAuditService _kycAuditService;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextService _httpContextService;
        private readonly TimeSpan _sessionTimeout;

        public KycSessionService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IKycAuditService kycAuditService,
            IHttpContextService httpContextService) : base(serviceProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _kycAuditService = kycAuditService ?? throw new ArgumentNullException(nameof(kycAuditService));
            _httpContextService = httpContextService ?? throw new ArgumentNullException(nameof(httpContextService));
            _sessionTimeout = TimeSpan.FromHours(_configuration.GetValue("KYC:SessionTimeoutHours", 24));
        }

        public async Task<ResultWrapper<KycSessionData>> GetOrCreateUserSessionAsync(Guid userId)
        {
            // Input validation
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("Invalid user ID", nameof(userId));
            }

            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "KycSessionService",
                    OperationName = "GetOrCreateUserSessionAsync(Guid userId)",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "CreateSession", $"KYC session requested");

                    // Check for existing active session
                    var existingSession = await GetActiveSession(userId);

                    if (existingSession != null)
                    {
                        // Extend session if within timeout
                        if (existingSession.ExpiresAt > DateTime.UtcNow)
                        {
                            existingSession.ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout);
                            await _repository.UpdateAsync(existingSession.Id, new Dictionary<string, object>
                            {
                                ["ExpiresAt"] = existingSession.ExpiresAt,
                                ["UpdatedAt"] = DateTime.UtcNow
                            });

                            return existingSession;
                        }
                        else
                        {
                            // Mark expired session as expired
                            await _repository.UpdateAsync(existingSession.Id, new Dictionary<string, object>
                            {
                                ["Status"] = "EXPIRED",
                                ["UpdatedAt"] = DateTime.UtcNow
                            });
                        }
                    }

                    // Create new session
                    var sessionData = new KycSessionData
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        SessionId = GenerateSecureSessionId(),
                        Status = "ACTIVE",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout),
                        SecurityContext = new SessionSecurityContext
                        {
                            IpAddress = _httpContextService.GetClientIpAddress(),
                            UserAgent = _httpContextService.GetUserAgent(),
                            CreatedAt = DateTime.UtcNow
                        }
                    };

                    var createResult = await InsertAsync(sessionData);
                    if (createResult == null || !createResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create KYC session: {createResult?.ErrorMessage ?? "Insert result returned null"}");
                    }

                    return sessionData;
                })
                .OnSuccess(async session =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "CreateSession", $"Session {session.SessionId} created successfully");
                })
                .OnError(async (ex) =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "CreateSession", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<KycSessionData>> ValidateSessionAsync(Guid sessionId, Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "KycSessionService",
                    OperationName = "ValidateSessionAsync(Guid sessionId, Guid userId)",
                    State = {
                        ["UserId"] = userId,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var filter = Builders<KycSessionData>.Filter.And(
                    Builders<KycSessionData>.Filter.Eq(s => s.Id, sessionId),
                    Builders<KycSessionData>.Filter.Eq(s => s.UserId, userId),
                    Builders<KycSessionData>.Filter.Eq(s => s.Status, "ACTIVE"),
                    Builders<KycSessionData>.Filter.Gt(s => s.ExpiresAt, DateTime.UtcNow)
                );

                    var sessionResult = await GetOneAsync(filter);
                    if (sessionResult == null || !sessionResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to retrieve KYC session: {sessionResult?.ErrorMessage ?? "Fetch result returned null"}");
                    }

                    var session = sessionResult.Data;

                    if (session == null)
                    {
                        throw new ResourceNotFoundException($"KYC session {sessionId} not found for user {userId} or has expired", sessionId.ToString());
                    }

                    // Update last accessed time and security context
                    var updateFields = new Dictionary<string, object>
                    {
                        ["UpdatedAt"] = DateTime.UtcNow,
                        ["SecurityContext.LastAccessedAt"] = DateTime.UtcNow,
                        ["SecurityContext.IpAddress"] = _httpContextService.GetClientIpAddress(),
                        ["SecurityContext.UserAgent"] = _httpContextService.GetUserAgent()
                    };

                    await _repository.UpdateAsync(session.Id, updateFields);

                    await _kycAuditService.LogAuditEvent(userId, "SessionValidated", $"Session {sessionId} validated successfully");

                    return session;
                })
                .OnError(async (ex) =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "SessionValidationError", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> InvalidateSessionAsync(Guid sessionId, Guid userId, string reason = "Manual invalidation")
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "KycSessionService",
                    OperationName = "InvalidateSessionAsync(Guid sessionId, Guid userId, string reason = \"Manual invalidation\")",
                    State = {
                        ["SessionId"] = sessionId,
                        ["UserId"] = userId,
                        ["Reason"] = reason,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Status"] = "EXPIRED",
                        ["UpdatedAt"] = DateTime.UtcNow,
                        ["CompletedAt"] = DateTime.UtcNow
                    };

                    var updateResult = await _repository.UpdateAsync(sessionId, updateFields);
                    if (updateResult == null || !updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to invalidate KYC session {sessionId}: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                    }
                })
                .OnSuccess(async () =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "SessionInvalidated", $"Session {sessionId} invalidated: {reason}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> UpdateSessionProgressAsync(Guid sessionId, SessionProgress progress)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "KycSessionService",
                    OperationName = "InvalidateSessionAsync(Guid sessionId, Guid userId, string reason = \"Manual invalidation\")",
                    State = {
                        ["SessionId"] = sessionId,
                        ["CurrentStep"] = progress.CurrentStep,
                        ["TotalSteps"] = progress.TotalSteps,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Progress"] = progress,
                        ["UpdatedAt"] = DateTime.UtcNow
                    };

                    var updateResult = await UpdateAsync(sessionId, updateFields);

                    if (updateResult == null || !updateResult.IsSuccess || !updateResult.Data.IsSuccess)
                        throw new DatabaseException($"Failed to update session progress: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                })
                .ExecuteAsync();
        }

        // Private helper methods

        private async Task<KycSessionData?> GetActiveSession(Guid userId)
        {
            try
            {
                var filter = Builders<KycSessionData>.Filter.And(
                Builders<KycSessionData>.Filter.Eq(s => s.UserId, userId),
                Builders<KycSessionData>.Filter.Eq(s => s.Status, "ACTIVE"),
                Builders<KycSessionData>.Filter.Gt(s => s.ExpiresAt, DateTime.UtcNow)
            );

                var sessionsResult = await GetManyAsync(filter);
                if (sessionsResult == null || !sessionsResult.IsSuccess)
                {
                    throw new DatabaseException($"Failed to retrieve active KYC sessions for user {userId}: {sessionsResult?.ErrorMessage ?? "Fetch result returned null"}");
                }
                var sessions = sessionsResult.Data;
                return sessions?.OrderByDescending(s => s.ExpiresAt).FirstOrDefault();
            }
            catch (Exception)
            {
                await _loggingService.LogTraceAsync($"Error retrieving active session for user {userId}", "GetActiveSession(Guid userId)", level: LogLevel.Warning);
                return null;
            }
        }

        private string GenerateSecureSessionId()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}