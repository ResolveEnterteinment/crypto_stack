using Application.Contracts.Requests.Payment;
using Application.Contracts.Responses;
using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.DTOs.Error;
using Domain.Models.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class ExchangeController : ControllerBase
    {
        private readonly IExchangeService _exchangeService;
        private readonly IPaymentProcessingService _paymentProcessingService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly IAssetService _assetService;
        private readonly ILogger<ExchangeController> _logger;

        public ExchangeController(
            IExchangeService exchangeService,
            IPaymentProcessingService paymentProcessingService,
            IBalanceManagementService balanceManagementService,
            IAssetService assetService,
            ILogger<ExchangeController> logger)
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _paymentProcessingService = paymentProcessingService ?? throw new ArgumentNullException(nameof(paymentProcessingService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes a payment for cryptocurrency purchase
        /// </summary>
        /// <param name="paymentRequest">Payment request details</param>
        /// <returns>Payment processing result</returns>
        /// <response code="200">Payment processed successfully</response>
        /// <response code="400">Invalid payment request</response>
        /// <response code="401">Unauthorized request</response>
        /// <response code="429">Too many requests</response>
        /// <response code="500">Internal server error</response>
        [HttpPost("payment")]
        [Authorize]
        [EnableRateLimiting("heavyOperations")]
        [ValidateAntiForgeryToken]
        [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ProcessPayment([FromBody] PaymentIntentRequest paymentRequest)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "ProcessPayment",
                ["UserId"] = paymentRequest?.UserId
            }))
            {
                try
                {
                    // Validate request
                    if (paymentRequest is null)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "A valid payment request is required.",
                            Code = "INVALID_PAYMENT_REQUEST",
                            TraceId = correlationId
                        });
                    }

                    // Validate user ID
                    if (string.IsNullOrEmpty(paymentRequest.UserId) || !Guid.TryParse(paymentRequest.UserId, out var userId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid user ID provided.",
                            Code = "INVALID_USER_ID",
                            TraceId = correlationId
                        });
                    }

                    // Validate subscription ID
                    if (string.IsNullOrEmpty(paymentRequest.SubscriptionId) || !Guid.TryParse(paymentRequest.SubscriptionId, out var subscriptionId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid subscription ID provided.",
                            Code = "INVALID_SUBSCRIPTION_ID",
                            TraceId = correlationId
                        });
                    }

                    // Map request to domain model
                    var paymentData = new PaymentData
                    {
                        UserId = userId,
                        SubscriptionId = subscriptionId,
                        Provider = paymentRequest.Provider,
                        PaymentProviderId = paymentRequest.PaymentId,
                        InvoiceId = paymentRequest.InvoiceId,
                        PaymentProviderFee = paymentRequest.PaymentProviderFee,
                        TotalAmount = paymentRequest.TotalAmount,
                        PlatformFee = paymentRequest.PlatformFee,
                        NetAmount = paymentRequest.NetAmount,
                        Currency = paymentRequest.Currency,
                        Status = paymentRequest.Status,
                    };

                    _logger.LogInformation(
                        "Processing payment of {Amount} {Currency} for user {UserId}, subscription {SubscriptionId}",
                        paymentData.TotalAmount, paymentData.Currency, paymentData.UserId, paymentData.SubscriptionId);

                    // Process payment
                    var processResult = await _paymentProcessingService.ProcessPayment(paymentData);

                    if (processResult == null || !processResult.IsSuccess || processResult.Data == null)
                    {
                        throw new InvalidOperationException(processResult?.ErrorMessage ?? "Payment processing returned null result.");
                    }
                    return processResult.ToActionResult(this);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing payment: {ErrorMessage}", ex.Message);

                    return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                    {
                        Message = "An error occurred while processing the payment.",
                        Code = "PAYMENT_PROCESSING_ERROR",
                        TraceId = correlationId
                    });
                }
            }
        }

        /// <summary>
        /// Gets current price for a specific asset
        /// </summary>
        /// <param name="ticker">Asset ticker symbol</param>
        /// <param name="exchange">Exchange name (optional)</param>
        /// <returns>Current asset price</returns>
        [HttpGet("price/{ticker}")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAssetPrice(string ticker, [FromQuery] string? exchange = null)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                if (string.IsNullOrEmpty(ticker))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Asset ticker is required",
                        Code = "MISSING_TICKER",
                        TraceId = correlationId
                    });
                }

                // Get asset from database to determine exchange
                var assetResult = await _assetService.GetByTickerAsync(ticker);
                if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                {
                    return assetResult.ToActionResult(this);
                }
                var asset = assetResult.Data;

                // Use provided exchange or default to asset's exchange
                var exchangeName = !string.IsNullOrEmpty(exchange) ? exchange : asset.Exchange;

                // Verify exchange exists
                if (!_exchangeService.Exchanges.ContainsKey(exchangeName))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = $"Exchange {exchangeName} not supported",
                        Code = "EXCHANGE_NOT_SUPPORTED",
                        TraceId = correlationId
                    });
                }

                // Get price from exchange
                var priceResult = await _exchangeService.Exchanges[exchangeName].GetAssetPrice(ticker);

                if (priceResult == null || !priceResult.IsSuccess)
                {
                    return priceResult.ToActionResult(this);
                }

                return Ok(priceResult.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching price for {Ticker}: {ErrorMessage}", ticker, ex.Message);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while fetching the asset price",
                    Code = "PRICE_FETCH_ERROR",
                    TraceId = correlationId
                });
            }
        }

        /// <summary>
        /// Gets a list of supported exchanges
        /// </summary>
        /// <returns>List of supported exchanges</returns>
        [HttpGet("exchanges")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
        public IActionResult GetSupportedExchanges()
        {
            try
            {
                var exchanges = _exchangeService.Exchanges.Keys.ToList();
                return Ok(exchanges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching supported exchanges: {ErrorMessage}", ex.Message);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while fetching supported exchanges",
                    Code = "EXCHANGES_FETCH_ERROR",
                    TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
                });
            }
        }
    }
}