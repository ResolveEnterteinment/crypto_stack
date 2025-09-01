using crypto_investment_project.Server.Configuration;
using crypto_investment_project.Server.Configuration.Idempotency;
using crypto_investment_project.Server.Middleware;
using Encryption;
using HealthChecks.UI.Client;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Connections;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine("🚀 Starting StackFi Server...");
Console.WriteLine($"🌍 Environment: {builder.Environment.EnvironmentName}");

// Register a global serializer to ensure GUIDs are stored using the Standard representation
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

// Apply configuration through extension methods
builder.Services
    .AddAppSettings(builder.Configuration)
    .AddEncryptionServices()
    .AddIdentityConfiguration(builder.Configuration)
    .AddAuthenticationServices(builder.Configuration)
    .AddIdempotencyMiddleware(builder.Configuration)
    .AddCoreServices(builder.Environment)
    .AddCacheServices()
    .AddHttpContextServices()
    .AddKycServices(builder.Configuration)
    .AddRateLimitingPolicies(builder.Environment)
    .AddApiVersioningSupport()
    .AddHealthChecksServices(builder.Configuration)
    .ConfigureCorsPolicy(builder.Configuration)
    .AddSwaggerServices()
    .AddHostedServices(builder.Environment)
    .AddFlowEngine(builder.Configuration);

builder.Host.UseSerilog();

Console.WriteLine("✅ Services configured successfully");

// Build the application
var app = builder.Build();

Console.WriteLine("🔧 Configuring middleware pipeline...");

// Initialize default roles
app.InitializeDefaultRoles();

// Configure the HTTP request pipeline
app.UseGlobalExceptionHandling();
app.UseTraceContext();
app.UseActivityNaming();
app.UseTraceUserEnrichment();
app.UseTraceException();
app.UseTraceIdResponse();

// Static files and development tools
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Configure web sockets for SignalR
app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Add security headers
app.UseSecurityHeaders();

// Add rate limiting
app.UseRateLimiter();

// Add CORS
app.UseCors("AllowSpecifiedOrigins");
Console.WriteLine("✅ CORS configured");

// Configure antiforgery
app.UseCustomAntiforgery();
Console.WriteLine("✅ CSRF protection configured");

// Authentication and authorization
app.UseAuthentication();
app.UseKycRequirement();
app.UseAuthorization();
Console.WriteLine("✅ Authentication configured");

app.UseHttpsRedirection();
app.UseIdempotency();
Console.WriteLine("✅ Middleware pipeline configured");

// Configure health checks endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    ResultStatusCodes = {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("readiness"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// Map SignalR hubs
app.MapHub<NotificationHub>("/hubs/notificationHub");
app.MapHub<DashboardHub>("/hubs/dashboardHub");
app.MapHub<FlowHub>("/hubs/flowHub", options =>
{
    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
});
Console.WriteLine("✅ SignalR hubs configured");

// Map diagnostic endpoint
app.MapGet("/api/diagnostic/connection", (HttpContext context) => Results.Ok(new
{
    timestamp = DateTime.UtcNow,
    serverTime = DateTime.UtcNow.ToString("o"),
    clientIp = context.Connection.RemoteIpAddress?.ToString(),
    headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString())
}));

// Map controllers and fallback
app.MapControllers();
app.MapFallbackToFile("/index.html");

Console.WriteLine("✅ Middleware pipeline configured successfully");
Console.WriteLine($"🌐 Server ready!");
Console.WriteLine($"📍 Test CSRF: https://localhost:7144/api/v1/csrf/test");
Console.WriteLine($"🔄 CSRF Refresh: https://localhost:7144/api/v1/csrf/refresh");

// Start the application
app.Run();