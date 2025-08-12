using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using crypto_investment_project.Server.Middleware;
using Domain.Settings;
using Application.Interfaces;

namespace crypto_investment_project.Tests.Integration
{
    public class IdempotencyMiddlewareAdditionalTests
    {
        private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
        private readonly Mock<ILogger<IdempotencyMiddleware>> _loggerMock;
        private readonly IMemoryCache _memoryCache;

        public IdempotencyMiddlewareAdditionalTests()
        {
            _idempotencyServiceMock = new Mock<IIdempotencyService>();
            _loggerMock = new Mock<ILogger<IdempotencyMiddleware>>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
        }

        [Fact]
        public async Task Should_Skip_Idempotency_For_GET_Requests()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = true,
                Methods = new List<string> { "POST", "PUT", "PATCH", "DELETE" },
                EnableMetrics = false
            };

            var middleware = CreateMiddleware(settings);
            var context = CreateHttpContext("GET", "/api/test");

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(200);
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()),
                Times.Never
            );
        }

        [Fact]
        public async Task Should_Skip_Idempotency_For_Excluded_Paths()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = true,
                Methods = new List<string> { "POST", "PUT", "PATCH", "DELETE" },
                ExcludedPaths = new List<string> { "/health", "/swagger" },
                EnableMetrics = false
            };

            var middleware = CreateMiddleware(settings);
            var context = CreateHttpContext("POST", "/health/ready");

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(200);
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()),
                Times.Never
            );
        }

        [Fact]
        public async Task Should_Auto_Generate_Key_When_Enabled()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                AutoGenerateKey = true,
                Methods = new List<string> { "POST" },
                EnableMetrics = false
            };

            var middleware = CreateMiddleware(settings);
            var context = CreateHttpContext("POST", "/api/test");
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"data\":\"test\"}"));

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()))
                .ReturnsAsync((false, (IdempotentResponse)null));

            _idempotencyServiceMock
                .Setup(x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()))
                .Returns(Task.CompletedTask);

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(200);
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()),
                Times.Once
            );
            _idempotencyServiceMock.Verify(
                x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()),
                Times.Once
            );
        }

        [Fact]
        public async Task Should_Validate_Key_Format_When_Validation_Enabled()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                ValidateKeyFormat = true,
                KeyFormatPattern = @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$",
                Methods = new List<string> { "POST" },
                EnableMetrics = false
            };

            var middleware = CreateMiddleware(settings);
            var context = CreateHttpContext("POST", "/api/test");
            context.Request.Headers["X-Idempotency-Key"] = "invalid-key-format";

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(400);
            var responseBody = await GetResponseBody(context);
            responseBody.Should().Contain("Invalid idempotency key format");
        }

        [Fact]
        public async Task Should_Handle_Concurrent_Requests_With_Lock()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                AutoGenerateKey = false,
                Methods = new List<string> { "POST" },
                LockTimeoutSeconds = 1,
                LockRetryAttempts = 2,
                LockRetryDelayMs = 100,
                EnableMetrics = false
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var middleware = CreateMiddleware(settings, simulateSlowRequest: true);

            // Setup for first request to process slowly
            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((false, (IdempotentResponse)null));

            _idempotencyServiceMock
                .Setup(x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()))
                .Returns(Task.CompletedTask);

            // Act - Start first request
            var context1 = CreateHttpContext("POST", "/api/test");
            context1.Request.Headers["X-Idempotency-Key"] = idempotencyKey;
            var task1 = middleware.InvokeAsync(context1, _idempotencyServiceMock.Object);

            // Small delay to ensure first request acquires lock
            await Task.Delay(50);

            // Start second request with same key (should hit lock)
            var context2 = CreateHttpContext("POST", "/api/test");
            context2.Request.Headers["X-Idempotency-Key"] = idempotencyKey;
            var task2 = middleware.InvokeAsync(context2, _idempotencyServiceMock.Object);

            // Wait for both
            await Task.WhenAll(task1, task2);

            // Assert - One should succeed, one should get conflict
            var statuses = new[] { context1.Response.StatusCode, context2.Response.StatusCode };
            statuses.Should().Contain(200); // One succeeds
            statuses.Should().Contain(409); // One gets conflict
        }

        [Fact]
        public async Task Should_Not_Cache_Server_Error_Responses()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                Methods = new List<string> { "POST" },
                NonCacheableStatusCodes = new List<int> { 500, 502, 503, 504 },
                EnableMetrics = false
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var middleware = new IdempotencyMiddleware(
                next: async (context) =>
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync("{\"error\":\"Internal Server Error\"}");
                },
                _loggerMock.Object,
                Options.Create(settings),
                _memoryCache
            );

            var context = CreateHttpContext("POST", "/api/test");
            context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((false, (IdempotentResponse)null));

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(500);

            // Verify that StoreResultAsync was NOT called for 500 error
            _idempotencyServiceMock.Verify(
                x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()),
                Times.Never
            );
        }

        [Fact]
        public async Task Should_Respect_Max_Response_Body_Size()
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                Methods = new List<string> { "POST" },
                MaxResponseBodySize = 100, // Very small size for testing
                CacheResponseBody = true,
                EnableMetrics = false
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var largeResponse = new string('x', 200); // Larger than max size

            var middleware = new IdempotencyMiddleware(
                next: async (context) =>
                {
                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsync(largeResponse);
                },
                _loggerMock.Object,
                Options.Create(settings),
                _memoryCache
            );

            var context = CreateHttpContext("POST", "/api/test");
            context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((false, (IdempotentResponse)null));

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(200);

            // Verify that response was NOT cached due to size limit
            _idempotencyServiceMock.Verify(
                x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()),
                Times.Never
            );

            // Verify warning was logged
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("too large to cache")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once
            );
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public async Task Should_Process_All_Configured_Http_Methods(string method)
        {
            // Arrange
            var settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                Methods = new List<string> { "POST", "PUT", "PATCH", "DELETE" },
                EnableMetrics = false
            };

            var idempotencyKey = Guid.NewGuid().ToString();
            var middleware = CreateMiddleware(settings);
            var context = CreateHttpContext(method, "/api/test");
            context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((false, (IdempotentResponse)null));

            _idempotencyServiceMock
                .Setup(x => x.StoreResultAsync(It.IsAny<string>(), It.IsAny<IdempotentResponse>(), It.IsAny<TimeSpan?>()))
                .Returns(Task.CompletedTask);

            // Act
            await middleware.InvokeAsync(context, _idempotencyServiceMock.Object);

            // Assert
            context.Response.StatusCode.Should().Be(200);
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(idempotencyKey),
                Times.Once
            );
        }

        // Helper methods
        private IdempotencyMiddleware CreateMiddleware(IdempotencySettings settings, bool simulateSlowRequest = false)
        {
            return new IdempotencyMiddleware(
                next: async (context) =>
                {
                    if (simulateSlowRequest)
                    {
                        await Task.Delay(300); // Simulate slow processing
                    }
                    context.Response.StatusCode = 200;
                    await context.Response.WriteAsync("{\"success\":true}");
                },
                _loggerMock.Object,
                Options.Create(settings),
                _memoryCache
            );
        }

        private DefaultHttpContext CreateHttpContext(string method, string path)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private async Task<string> GetResponseBody(HttpContext context)
        {
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(context.Response.Body);
            return await reader.ReadToEndAsync();
        }
    }
}