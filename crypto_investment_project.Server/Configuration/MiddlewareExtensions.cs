using Microsoft.AspNetCore.Antiforgery;

namespace crypto_investment_project.Server.Configuration;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("Content-Security-Policy",
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;");
            await next();
        });
    }

    public static IApplicationBuilder UseCustomAntiforgery(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            // Only for GET requests to set the cookie
            if (context.Request.Method == HttpMethods.Get)
            {
                var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
                // We set the cookie but don't need the token here
                antiforgery.GetAndStoreTokens(context);
            }

            await next();
        });
    }
}