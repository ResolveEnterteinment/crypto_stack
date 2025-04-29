using System.Diagnostics;

namespace crypto_investment_project.Server.Middleware
{
    public class ActivityNamingMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly ActivitySource _activitySource = new("MyApp");

        public ActivityNamingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            if (endpoint != null)
            {
                var routeValues = context.Request.RouteValues;
                if (routeValues.TryGetValue("controller", out var controller) &&
                    routeValues.TryGetValue("action", out var action))
                {
                    var activityName = $"{controller}.{action}";
                    var parentContext = Activity.Current?.Context ?? default;
                    using var activity = _activitySource.StartActivity(activityName, ActivityKind.Internal, parentContext);

                    if (activity != null)
                    {
                        activity.DisplayName = activityName;
                        activity.SetTag("http.controller", controller?.ToString());
                        activity.SetTag("http.action", action?.ToString());
                    }

                    await _next(context);
                    return;
                }
            }
            await _next(context);
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class ActivityNamingMiddlewareExtensions
    {
        public static IApplicationBuilder UseActivityNaming(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ActivityNamingMiddleware>();
        }
    }
}