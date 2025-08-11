using Application.Extensions;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Domain.DTOs.Error;
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
        /// Gets minimum notional value for a specific asset
        /// </summary>
        /// <param name="ticker">Asset ticker symbol</param>
        /// <param name="exchange">Exchange name (optional)</param>
        /// <returns>Minimum notional value required for orders</returns>
        [HttpGet("min-notional/{ticker}")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMinNotional(string ticker, [FromQuery] string? exchange = null)
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

                // Use the new service method with caching
                var minNotionalResult = await _exchangeService.GetMinNotionalAsync(ticker, exchange);

                if (minNotionalResult == null || !minNotionalResult.IsSuccess)
                {
                    return minNotionalResult.ToActionResult(this);
                }

                return Ok(minNotionalResult.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching minimum notional for {Ticker}: {ErrorMessage}", ticker, ex.Message);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while fetching the minimum notional value",
                    Code = "MIN_NOTIONAL_FETCH_ERROR",
                    TraceId = correlationId
                });
            }
        }

        /// <summary>
        /// Gets minimum notional values for multiple assets simultaneously
        /// </summary>
        /// <param name="tickers">Array of asset ticker symbols</param>
        /// <returns>Dictionary mapping tickers to their minimum notional values</returns>
        [HttpGet("min-notionals")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(Dictionary<string, decimal>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMinNotionals([FromQuery] string[] tickers)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                if (tickers == null || tickers.Length == 0)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "At least one ticker is required",
                        Code = "MISSING_TICKERS",
                        TraceId = correlationId
                    });
                }

                // Use the new service method with caching
                var minNotionalsResult = await _exchangeService.GetMinNotionalsAsync(tickers);

                if (minNotionalsResult == null || !minNotionalsResult.IsSuccess)
                {
                    return minNotionalsResult.ToActionResult(this);
                }

                return Ok(minNotionalsResult.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching min notionals for tickers: {ErrorMessage}", ex.Message);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while fetching minimum notional values",
                    Code = "MIN_NOTIONAL_FETCH_ERROR",
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