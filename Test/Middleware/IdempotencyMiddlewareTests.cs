using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Application.Interfaces;
using crypto_investment_project.Server.Middleware;
using Domain.Settings;

namespace crypto_investment_project.Tests.Middleware
{
    public class IdempotencyMiddlewareTests
    {
        private readonly Mock<IIdempotencyService> _idempotencyServiceMock;
        private readonly Mock<ILogger<IdempotencyMiddleware>> _loggerMock;
        private readonly IMemoryCache _memoryCache;
        private readonly IdempotencySettings _settings;
        private readonly IdempotencyMiddleware _middleware;
        private readonly DefaultHttpContext _context;

        public IdempotencyMiddlewareTests()
        {
            _idempotencyServiceMock = new Mock<IIdempotencyService>();
            _loggerMock = new Mock<ILogger<IdempotencyMiddleware>>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _settings = new IdempotencySettings
            {
                RequireIdempotencyKey = false,
                AutoGenerateKey = true,
                Methods = new List<string> { "POST", "PUT", "PATCH", "DELETE" }
            };

            var options = Options.Create(_settings);

            _middleware = new IdempotencyMiddleware(
                next: (innerHttpContext) => Task.CompletedTask,
                _loggerMock.Object,
                options,
                _memoryCache
            );

            _context = new DefaultHttpContext();
            _context.Response.Body = new MemoryStream();
        }

        [Fact]
        public async Task Should_Skip_Processing_For_GET_Requests()
        {
            // Arrange
            _context.Request.Method = "GET";
            _context.Request.Path = "/api/test";

            // Act
            await _middleware.InvokeAsync(_context, _idempotencyServiceMock.Object);

            // Assert
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<It.IsAnyType>(It.IsAny<string>()),
                Times.Never
            );
        }

        [Fact]
        public async Task Should_Process_POST_Request_With_IdempotencyKey()
        {
            // Arrange
            var idempotencyKey = Guid.NewGuid().ToString();
            _context.Request.Method = "POST";
            _context.Request.Path = "/api/test";
            _context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((false, null));

            // Act
            await _middleware.InvokeAsync(_context, _idempotencyServiceMock.Object);

            // Assert
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(idempotencyKey),
                Times.Once
            );
        }

        [Fact]
        public async Task Should_Return_Cached_Response_When_Key_Exists()
        {
            // Arrange
            var idempotencyKey = Guid.NewGuid().ToString();
            var cachedResponse = new IdempotentResponse
            {
                StatusCode = 200,
                Body = "{\"id\":\"123\",\"message\":\"Success\"}",
                Headers = new Dictionary<string, string>(),
                Timestamp = DateTime.UtcNow
            };

            _context.Request.Method = "POST";
            _context.Request.Path = "/api/test";
            _context.Request.Headers["X-Idempotency-Key"] = idempotencyKey;

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(idempotencyKey))
                .ReturnsAsync((true, cachedResponse));

            // Act
            await _middleware.InvokeAsync(_context, _idempotencyServiceMock.Object);

            // Assert
            _context.Response.StatusCode.Should().Be(200);
            _context.Response.Headers.Should().ContainKey("X-Idempotent-Response");

            _context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = new StreamReader(_context.Response.Body).ReadToEnd();
            responseBody.Should().Be(cachedResponse.Body);
        }

        [Fact]
        public async Task Should_Validate_Key_Format_When_Enabled()
        {
            // Arrange
            _settings.ValidateKeyFormat = true;
            _settings.KeyFormatPattern = @"^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$";

            var invalidKey = "invalid-key-format";
            _context.Request.Method = "POST";
            _context.Request.Path = "/api/test";
            _context.Request.Headers["X-Idempotency-Key"] = invalidKey;

            var middleware = new IdempotencyMiddleware(
                next: (innerHttpContext) => Task.CompletedTask,
                _loggerMock.Object,
                Options.Create(_settings),
                _memoryCache
            );

            // Act
            await middleware.InvokeAsync(_context, _idempotencyServiceMock.Object);

            // Assert
            _context.Response.StatusCode.Should().Be(400);
            _context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseBody = new StreamReader(_context.Response.Body).ReadToEnd();
            responseBody.Should().Contain("Invalid idempotency key format");
        }

        [Fact]
        public async Task Should_Auto_Generate_Key_When_Enabled_And_No_Key_Provided()
        {
            // Arrange
            _settings.AutoGenerateKey = true;
            _context.Request.Method = "POST";
            _context.Request.Path = "/api/test";
            _context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"test\":\"data\"}"));

            _idempotencyServiceMock
                .Setup(x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()))
                .ReturnsAsync((false, null));

            // Act
            await _middleware.InvokeAsync(_context, _idempotencyServiceMock.Object);

            // Assert
            _idempotencyServiceMock.Verify(
                x => x.GetResultAsync<IdempotentResponse>(It.IsAny<string>()),
                Times.Once
            );
        }
    }
}