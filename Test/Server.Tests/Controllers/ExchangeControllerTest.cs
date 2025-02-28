using Application.Interfaces;
using crypto_investment_project.Server.Controllers;
using Domain.Constants;
using Domain.DTOs;
using Domain.Models.Transaction;
using Infrastructure.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
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
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest();
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);
            var expectedResponses = new AllocationOrdersResult(new List<OrderResult>(){
                OrderResult.Success(
                    orderId: 123456,
                    isInsertSuccess: true,
                    isUpdateSuccess: true,
                    coinId: ObjectId.GenerateNewId().ToString(),
                    transactionData.NetAmount,
                    0.0096m,
                    OrderStatus.Filled
                    ),
            });

            var mockExchangeService = new Mock<IExchangeService>();
            mockExchangeService.Setup(x => x.ProcessTransaction(It.IsAny<TransactionData>()))
                               .ReturnsAsync(expectedResponses);

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(exchangeRequest);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(expectedResponses, okResult?.Value);
        }

        [Fact]
        public async Task Post_ProcessTransactionReturnsNull_ReturnsBadRequest()
        {
            // Arrange
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest();
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);

            var mockExchangeService = new Mock<IExchangeService>();
            // Simulate ProcessTransaction returning null.
            mockExchangeService.Setup(x => x.ProcessTransaction(transactionData))
                               .ReturnsAsync((AllocationOrdersResult?)null);

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(exchangeRequest);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Exchange order could not be initiated", badRequestResult.Value.ToString());
        }

        [Fact]
        public async Task Post_ProcessTransactionThrowsException_ReturnsBadRequest()
        {
            // Arrange
            var exchangeRequest = TestDataFactory.CreateDefaultExchangeRequest();
            var transactionData = TestDataFactory.CreateDefaultTransactionData(exchangeRequest);
            var exceptionMessage = "Exchange order could not be initiated";

            var mockExchangeService = new Mock<IExchangeService>();
            mockExchangeService.Setup(x => x.ProcessTransaction(transactionData))
                               .ThrowsAsync(new Exception(exceptionMessage));

            var controller = new ExchangeController(mockExchangeService.Object);

            // Act
            var result = await controller.Post(exchangeRequest);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains(exceptionMessage, badRequestResult.Value?.ToString());
        }
    }
}
