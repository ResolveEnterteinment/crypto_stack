using Serilog.Context;
using System.Diagnostics;

namespace crypto_investment_project.Server.Middleware
{
    public class TraceUserEnrichmentMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceUserEnrichmentMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var activity = Activity.Current;

            if (activity != null && context.User?.Identity?.IsAuthenticated == true)
            {
                var userId = context.User.Identity?.Name ?? "Unknown";

                if (!string.IsNullOrEmpty(userId))
                {
                    activity.SetTag("user.id", userId);
                    LogContext.PushProperty("UserId", userId);
                }
            }

            await _next(context);
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class TraceUserEnrichmentExtensions
    {
        public static IApplicationBuilder UseTraceUserEnrichment(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TraceUserEnrichmentMiddleware>();
        }
    }
}