using System.Diagnostics;

namespace crypto_investment_project.Server.Middleware
{
    public class TraceIdResponseMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceIdResponseMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            await _next(context);

            var activity = Activity.Current;
            if (activity != null)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers.TryAdd("X-Trace-Id", activity.TraceId.ToString());
                    context.Response.Headers.TryAdd("X-Span-Id", activity.SpanId.ToString());
                }
            }
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class TraceIdResponseMiddlewareExtensions
    {
        public static IApplicationBuilder UseTraceIdResponse(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TraceIdResponseMiddleware>();
        }
    }
}