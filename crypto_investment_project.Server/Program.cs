using crypto_investment_project.Server.Configuration;
using crypto_investment_project.Server.Middleware;
using Encryption;
using HealthChecks.UI.Client;
using Infrastructure.Hubs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Apply configuration through extension methods
builder.Services
    .AddAppSettings(builder.Configuration)
    .AddEncryptionServices()
    .AddIdentityConfiguration(builder.Configuration)
    .AddAuthenticationServices(builder.Configuration)
    .AddCoreServices(builder.Environment)
    .AddRateLimitingPolicies()
    .AddApiVersioningSupport()
    .AddHealthChecksServices(builder.Configuration)
    .ConfigureCorsPolicy(builder.Environment, builder.Configuration)
    .AddSwaggerServices()
    .AddHostedServices(builder.Environment);

builder.Host.UseSerilog();

// Build the application
var app = builder.Build();

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
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Add security headers
app.UseSecurityHeaders();

// Add rate limiting
app.UseRateLimiter();

// Add CORS
app.UseCors("AllowSpecifiedOrigins");

// Configure antiforgery
app.UseCustomAntiforgery();

// Authentication and authorization
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();

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

// Map SignalR hub
app.MapHub<NotificationHub>("/hubs/notificationHub");

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

// Start the application
app.Run();