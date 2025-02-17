using Application.Contracts.Responses.Exchange;
using Application.Interfaces;
using crypto_investment_project.Server.Controllers;
using Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Server.Tests.Controllers
{
    public class ExchangeControllerTests
    {
        [Fact]
        public async Task Post_NullTransactionData_ReturnsBadRequest()
        {
            // Arrange
            var mockExchangeService = new Mock<IExchangeService>();
            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(null);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("A valid transaction is required.", badRequestResult.Value);
        }

        [Fact]
        public async Task Post_ProcessTransactionReturnsValid_ReturnsOkResult()
        {
            // Arrange
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            var expectedResponses = new List<ExchangeOrderResponse>
            {
                new ExchangeOrderResponse(true, "Order created successfully")
            };

            var mockExchangeService = new Mock<IExchangeService>();
            mockExchangeService.Setup(x => x.ProcessTransaction(transaction))
                               .ReturnsAsync(expectedResponses);

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(transaction);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedResponses, okResult.Value);
        }

        [Fact]
        public async Task Post_ProcessTransactionReturnsNull_ReturnsBadRequest()
        {
            // Arrange
            var transaction = TestDataFactory.CreateDefaultTransactionData();

            var mockExchangeService = new Mock<IExchangeService>();
            // Simulate ProcessTransaction returning null.
            mockExchangeService.Setup(x => x.ProcessTransaction(transaction))
                               .ReturnsAsync((IEnumerable<ExchangeOrderResponse>?)null);

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(transaction);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Exchange order could not be initiated", badRequestResult.Value.ToString());
        }

        [Fact]
        public async Task Post_ProcessTransactionThrowsException_ReturnsBadRequest()
        {
            // Arrange
            var transaction = TestDataFactory.CreateDefaultTransactionData();
            var exceptionMessage = "Some error occurred";

            var mockExchangeService = new Mock<IExchangeService>();
            mockExchangeService.Setup(x => x.ProcessTransaction(transaction))
                               .ThrowsAsync(new Exception(exceptionMessage));

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(transaction);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains(exceptionMessage, badRequestResult.Value.ToString());
        }
    }
}
