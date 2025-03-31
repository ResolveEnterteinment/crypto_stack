using System.Text.Json;

namespace crypto_investment_project.Server.Middleware
{
    public class SignalRCorsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SignalRCorsMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public SignalRCorsMiddleware(
            RequestDelegate next,
            ILogger<SignalRCorsMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Add debug headers for WebSocket diagnostics
            if (context.Request.Path.StartsWithSegments("/hubs"))
            {
                _logger.LogDebug(
                    "SignalR request received: {Path}, Headers: {Headers}, WebSocketRequested: {WebSocketRequested}",
                    context.Request.Path,
                    JsonSerializer.Serialize(context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())),
                    context.WebSockets.IsWebSocketRequest);

                // Ensure CORS headers are applied for WebSocket requests
                var origins = _configuration.GetSection("AllowedOrigins").Get<string[]>()
                    ?? new[] { "https://localhost:5173" };

                var origin = context.Request.Headers["Origin"].ToString();
                if (!string.IsNullOrEmpty(origin))
                {
                    // Allow the specific origin that made this request (if it's in our allowed list)
                    if (origins.Contains(origin) || _configuration["Environment"] == "Development")
                    {
                        context.Response.Headers.Add("Access-Control-Allow-Origin", origin);
                        context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                        context.Response.Headers.Add("Access-Control-Allow-Headers", "content-type,authorization");
                        context.Response.Headers.Add("Access-Control-Allow-Methods", "GET,POST,OPTIONS");

                        // Handle preflight requests
                        if (context.Request.Method == "OPTIONS")
                        {
                            context.Response.StatusCode = 200;
                            await context.Response.WriteAsync("OK");
                            return;
                        }
                    }
                }
            }

            // Continue processing the request
            await _next(context);
        }
    }

    // Extension method to easily add the middleware
    public static class SignalRCorsMiddlewareExtensions
    {
        public static IApplicationBuilder UseSignalRCors(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SignalRCorsMiddleware>();
        }
    }
}