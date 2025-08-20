using Application.Extensions;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using CryptoExchange.Net.CommonObjects;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Error;
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
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets current price for a specific asset
        /// </summary>
        /// <param name="ticker">Asset ticker symbol</param>
        /// <returns>Current asset price</returns>
        [HttpGet("price/{ticker}")]
        [Authorize]
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
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                    "Asset ticker is required",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
                }

                // Get asset from database to determine exchange
                var assetResult = await _assetService.GetByTickerAsync(ticker);

                if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                {
                    return ResultWrapper.NotFound("Asset", ticker)
                        .ToActionResult(this);
                }

                var asset = assetResult.Data;

                // Use provided exchange or default to asset's exchange
                var exchangeName = !string.IsNullOrEmpty(exchange) ? exchange : asset.Exchange;

                // Verify exchange exists
                if (!_exchangeService.Exchanges.ContainsKey(exchangeName))
                {
                    if (string.IsNullOrEmpty(ticker))
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                        $"Exchange {exchangeName} not supported",
                        "INVALID_REQUEST")
                        .ToActionResult(this);
                    }
                }

                // Get price from exchange
                var priceResult = await _exchangeService.Exchanges[exchangeName].GetAssetPrice(ticker);

                if (priceResult == null || !priceResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.ExchangeApiError, 
                        $"Failed to fetch asset price for {ticker}",
                        "PRICE_FETCH_ERROR")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(priceResult.Data, 
                    "Asset price retrieved successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching price for {Ticker}: {ErrorMessage}", ticker, ex.Message);

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets current prices for a collection of assets
        /// </summary>
        /// <param name="ticker">Asset ticker symbols</param>
        /// <returns>Current asset price</returns>
        [HttpGet("prices/{ticker}")]
        [Authorize]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(decimal), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetAssetPrices([FromQuery] string[] tickers)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            try
            {
                if (tickers.Length == 0 || !tickers.All(t => !string.IsNullOrWhiteSpace(t)))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                    "Asset tickers is required and can not contain any empty elements",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
                }

                // Get price from exchange
                var pricesResult = await _exchangeService.GetCachedAssetPricesAsync(tickers);

                if (pricesResult == null || !pricesResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.ExchangeApiError,
                        $"Failed to fetch asset prices for {string.Join(',', tickers)}",
                        "PRICE_FETCH_ERROR")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(pricesResult.Data,
                    "Asset price retrieved successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching prices for {Tickers}: {ErrorMessage}", string.Join(',', tickers), ex.Message);

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
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
                    return ResultWrapper.Failure(FailureReason.ValidationError,
                    "Asset ticker is required",
                    "INVALID_REQUEST")
                    .ToActionResult(this);
                }

                // Use the new service method with caching
                var minNotionalResult = await _exchangeService.GetMinNotionalAsync(ticker, exchange);

                if (minNotionalResult == null || !minNotionalResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.ExchangeApiError,
                        $"Failed to fetch min notional for {ticker}",
                        "MIN_NOTIONAL_FETCH_ERROR")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(minNotionalResult.Data,
                    "Minimum notional value retrieved successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching minimum notional for {Ticker}: {ErrorMessage}", ticker, ex.Message);

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
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

                return ResultWrapper.Success(minNotionalsResult.Data,
                    "Minimum notional values retrieved successfully")
                    .ToActionResult(this);

                return Ok(minNotionalsResult.Data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching minimum notional for {Tickers}: {ErrorMessage}", string.Join('s', tickers), ex.Message);

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets a list of supported exchanges
        /// </summary>
        /// <returns>List of supported exchanges</returns>
        [HttpGet("supported")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
        public IActionResult GetSupportedExchanges()
        {
            try
            {
                var exchanges = _exchangeService.Exchanges.Keys.ToList();
                return ResultWrapper.Success(exchanges, "Supported exchanges retrieved successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching supported exchanges: {ErrorMessage}", ex.Message);

                return ResultWrapper.InternalServerError()
                .ToActionResult(this);
            }
        }
    }
}