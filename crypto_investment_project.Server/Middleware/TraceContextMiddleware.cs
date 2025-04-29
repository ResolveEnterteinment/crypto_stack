using Serilog.Context;
using System.Diagnostics;

namespace crypto_investment_project.Server.Middleware
{
    public class TraceContextMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceContextMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault() ?? Guid.NewGuid().ToString();
            var parentId = context.Request.Headers["X-Parent-Correlation-ID"].FirstOrDefault();

            // Store into HttpContext
            context.Items["CorrelationId"] = correlationId;
            context.Items["ParentCorrelationId"] = parentId;

            // Push into Serilog LogContext
            LogContext.PushProperty("correlation.id", correlationId);
            if (!string.IsNullOrEmpty(parentId))
                LogContext.PushProperty("parent.correlation.id", parentId);

            // Enrich Activity if available
            var activity = Activity.Current;
            if (activity != null)
            {
                // set tags
                activity.SetTag("correlation.id", correlationId);
                if (!string.IsNullOrEmpty(parentId))
                    activity.SetTag("parent.correlation.id", parentId);

                // also tag controller/action for better trace naming
                var endpoint = context.GetEndpoint();
                if (endpoint != null)
                {
                    var rv = context.Request.RouteValues;
                    if (rv.TryGetValue("controller", out var c)) activity.SetTag("http.controller", c?.ToString());
                    if (rv.TryGetValue("action", out var a)) activity.SetTag("http.action", a?.ToString());
                }
            }

            await _next(context);
        }
    }

    public static class TraceContextMiddlewareExtensions
    {
        public static IApplicationBuilder UseTraceContext(this IApplicationBuilder builder)
            => builder.UseMiddleware<TraceContextMiddleware>();
    }
}
