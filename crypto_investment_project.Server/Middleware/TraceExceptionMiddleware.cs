using OpenTelemetry.Trace;
using System.Diagnostics;

namespace crypto_investment_project.Server.Middleware
{
    public class TraceExceptionMiddleware
    {
        private readonly RequestDelegate _next;

        public TraceExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var activity = Activity.Current;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                if (activity != null)
                {
                    activity.SetStatus(ActivityStatusCode.Error);
                    activity.SetTag("otel.status_code", "ERROR");
                    activity.SetTag("otel.status_description", ex.Message);
                    activity.RecordException(ex);
                }

                throw; // rethrow the exception to still fail correctly
            }
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class TraceExceptionMiddlewareExtensions
    {
        public static IApplicationBuilder UseTraceException(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TraceExceptionMiddleware>();
        }
    }
}