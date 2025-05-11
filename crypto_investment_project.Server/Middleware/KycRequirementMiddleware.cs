// crypto_investment_project.Server/Middleware/KycRequirementMiddleware.cs
using Application.Interfaces.KYC;
using Application.Interfaces.Withdrawal;
using Domain.Constants.KYC;
using Domain.DTOs.Withdrawal;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace crypto_investment_project.Server.Middleware
{
    public class KycRequirementMiddleware
    {
        private readonly RequestDelegate _next;

        public KycRequirementMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IKycService kycService, IWithdrawalService withdrawalService)
        {
            // Skip for non-API requests or paths that don't require KYC
            if (!context.Request.Path.StartsWithSegments("/api/withdrawal"))
            {
                await _next(context);
                return;
            }

            // Skip KYC check for unauthenticated requests (they'll be rejected by auth checks anyway)
            if (!context.User.Identity.IsAuthenticated)
            {
                await _next(context);
                return;
            }

            // Skip KYC check for admin users
            if (context.User.IsInRole("ADMIN"))
            {
                await _next(context);
                return;
            }

            // Check withdrawal limits for withdrawal requests
            if (context.Request.Path.StartsWithSegments("/api/withdrawal/request"))
            {
                await CheckWithdrawalLimits(context, kycService, withdrawalService);
            }

            // Get the user ID from claims
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid user ID" });
                return;
            }

            // Check if the user is verified
            var requiredLevel = GetRequiredKycLevelForPath(context.Request.Path);
            if (requiredLevel != null)
            {
                var verificationResult = await kycService.IsUserVerifiedAsync(parsedUserId, requiredLevel);

                if (!verificationResult.IsSuccess || !verificationResult.Data)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "KYC verification required",
                        requiredLevel = requiredLevel,
                        code = "KYC_REQUIRED"
                    });
                    return;
                }
            }

            // Continue with the request
            await _next(context);
        }

        private string GetRequiredKycLevelForPath(PathString path)
        {
            // Trading API requires Standard KYC
            if (path.StartsWithSegments("/api/exchange") ||
                path.StartsWithSegments("/api/transaction") ||
                path.StartsWithSegments("/api/balance"))
            {
                return KycLevel.Standard;
            }

            // Subscription API requires Basic KYC
            if (path.StartsWithSegments("/api/subscription"))
            {
                return KycLevel.Basic;
            }

            // Dashboard API requires Basic KYC
            if (path.StartsWithSegments("/api/dashboard"))
            {
                return KycLevel.Basic;
            }

            // Other APIs don't require KYC
            return null;
        }

        private async Task CheckWithdrawalLimits(HttpContext context, IKycService kycService, IWithdrawalService withdrawalService)
        {
            // Only check for withdrawal requests
            if (!context.Request.Path.StartsWithSegments("/api/withdrawal/request") ||
                context.Request.Method != "POST")
            {
                return;
            }

            // Get the user ID from claims
            var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var parsedUserId))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid user ID" });
                return;
            }

            // Get the withdrawal amount from request body
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, true))
            {
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;  // Reset position for next middleware
            }

            var requestData = JsonSerializer.Deserialize<WithdrawalRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (requestData == null || requestData.Amount <= 0)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid withdrawal request" });
                return;
            }

            // Check if user can withdraw this amount
            var canWithdrawResult = await withdrawalService.CanUserWithdrawAsync(parsedUserId, requestData.Amount);
            if (!canWithdrawResult.IsSuccess || !canWithdrawResult.Data)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = canWithdrawResult.IsSuccess ? canWithdrawResult.DataMessage : "Withdrawal not allowed",
                    code = "WITHDRAWAL_LIMIT_EXCEEDED"
                });
                return;
            }

            // Continue with the request since limits are OK
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class KycRequirementMiddlewareExtensions
    {
        public static IApplicationBuilder UseKycRequirement(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<KycRequirementMiddleware>();
        }
    }
}