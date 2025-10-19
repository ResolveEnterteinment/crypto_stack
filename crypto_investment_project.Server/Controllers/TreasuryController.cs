using Application.Extensions;
using Application.Interfaces.Treasury;
using Domain.DTOs;
using Domain.DTOs.Treasury;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    /// <summary>
    /// Controller for corporate treasury operations
    /// Admin-only access for viewing and managing treasury data
    /// </summary>
    [Authorize(Roles = "ADMIN")]
    [ApiController]
    [Route("api/[controller]")]
    public class TreasuryController : ControllerBase
    {
        private readonly ITreasuryService _treasuryService;
        private readonly ITreasuryBalanceService _treasuryBalanceService;
        private readonly ILogger<TreasuryController> _logger;

        public TreasuryController(
            ITreasuryService treasuryService,
            ITreasuryBalanceService treasuryBalanceService,
            ILogger<TreasuryController> logger)
        {
            _treasuryService = treasuryService ?? throw new ArgumentNullException(nameof(treasuryService));
            _treasuryBalanceService = treasuryBalanceService ?? throw new ArgumentNullException(nameof(treasuryBalanceService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Get treasury summary for a date range
        /// </summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(TreasurySummaryDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSummary(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken cancellationToken)
        {
            try
            {
                var validationErrors = new Dictionary<string, string[]>();
                if (startDate > endDate)
                {
                    return ResultWrapper.ValidationError(new Dictionary<string, string[]>
                    {
                        { "StartDate", ["Start date must be before end date"] }
                    }).ToActionResult(this);
                }

                if ((endDate - startDate).TotalDays > 365)
                {
                    return ResultWrapper.ValidationError(new Dictionary<string, string[]>
                    {
                        { "EndDate", ["Date range cannot exceed 365 days"] }
                    }).ToActionResult(this);
                }

                var summary = await _treasuryService.GetSummaryAsync(
                    startDate,
                    endDate,
                    cancellationToken);

                return ResultWrapper.Success(summary)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting treasury summary", ex);
                return ResultWrapper.InternalServerError()
                    .ToActionResult(this);
            }
        }

        /// <summary>
        /// Get all treasury balances
        /// </summary>
        [HttpGet("balances")]
        [ProducesResponseType(typeof(List<Domain.Models.Treasury.TreasuryBalanceData>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllBalances(CancellationToken cancellationToken)
        {
            try
            {
                var balances = await _treasuryBalanceService.GetAllBalancesAsync(cancellationToken);
                return Ok(balances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting treasury balances");
                return StatusCode(500, "An error occurred while retrieving treasury balances");
            }
        }

        /// <summary>
        /// Get treasury balance for a specific asset
        /// </summary>
        [HttpGet("balances/{assetTicker}")]
        [ProducesResponseType(typeof(Domain.Models.Treasury.TreasuryBalanceData), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetBalanceByAsset(
            string assetTicker,
            CancellationToken cancellationToken)
        {
            try
            {
                var balance = await _treasuryBalanceService.GetBalanceByAssetAsync(
                    assetTicker,
                    cancellationToken);

                if (balance == null)
                {
                    return NotFound($"No treasury balance found for asset {assetTicker}");
                }

                return Ok(balance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting treasury balance for {AssetTicker}", assetTicker);
                return StatusCode(500, "An error occurred while retrieving treasury balance");
            }
        }

        /// <summary>
        /// Get revenue breakdown by source
        /// </summary>
        [HttpGet("breakdown")]
        [ProducesResponseType(typeof(List<TreasuryBreakdownDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBreakdownBySource(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken cancellationToken)
        {
            try
            {
                if (startDate > endDate)
                {
                    return BadRequest("Start date must be before end date");
                }

                var breakdown = await _treasuryService.GetBreakdownBySourceAsync(
                    startDate,
                    endDate,
                    cancellationToken);

                return Ok(breakdown);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting treasury breakdown");
                return StatusCode(500, "An error occurred while retrieving treasury breakdown");
            }
        }

        /// <summary>
        /// Get transaction history with filters
        /// </summary>
        [HttpGet("transactions")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTransactionHistory(
            [FromQuery] TreasuryTransactionFilter filter,
            CancellationToken cancellationToken,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50 )
        {
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1 || pageSize > 100) pageSize = 50;

                var paginatedResult = await _treasuryService.GetTransactionHistoryAsync(
                    filter,
                    page,
                    pageSize,
                    cancellationToken);

                return Ok(paginatedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting transaction history");
                return StatusCode(500, "An error occurred while retrieving transaction history");
            }
        }

        /// <summary>
        /// Export treasury transactions
        /// </summary>
        [HttpGet("export")]
        public async Task<IActionResult> ExportTransactions(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            CancellationToken cancellationToken,
            [FromQuery] string format = "csv")
        {
            try
            {
                if (startDate > endDate)
                {
                    return BadRequest("Start date must be before end date");
                }

                var data = await _treasuryService.ExportTransactionsAsync(
                    startDate,
                    endDate,
                    format,
                    cancellationToken);

                var contentType = format.ToLower() switch
                {
                    "csv" => "text/csv",
                    _ => "application/octet-stream"
                };

                var fileName = $"treasury-export-{DateTime.UtcNow:yyyyMMddHHmmss}.{format}";

                return File(data, contentType, fileName);
            }
            catch (NotSupportedException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting treasury data");
                return StatusCode(500, "An error occurred while exporting treasury data");
            }
        }

        /// <summary>
        /// Mark transactions as reported
        /// </summary>
        [HttpPost("mark-reported")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkAsReported(
            [FromBody] MarkReportedRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (request.StartDate > request.EndDate)
                {
                    return BadRequest("Start date must be before end date");
                }

                if (string.IsNullOrWhiteSpace(request.ReportingPeriod))
                {
                    return BadRequest("Reporting period is required");
                }

                await _treasuryService.MarkAsReportedAsync(
                    request.StartDate,
                    request.EndDate,
                    request.ReportingPeriod,
                    cancellationToken);

                return Ok(new { message = "Transactions marked as reported successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking transactions as reported");
                return StatusCode(500, "An error occurred while marking transactions as reported");
            }
        }

        /// <summary>
        /// Validate treasury balance integrity
        /// </summary>
        [HttpGet("validate/{assetTicker}")]
        [ProducesResponseType(typeof(TreasuryValidationResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> ValidateBalance(
            string assetTicker,
            CancellationToken cancellationToken)
        {
            try
            {
                var validationResult = await _treasuryService.ValidateBalanceIntegrityAsync(
                    assetTicker,
                    cancellationToken);

                return Ok(validationResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating treasury balance for {AssetTicker}", assetTicker);
                return StatusCode(500, "An error occurred during validation");
            }
        }

        /// <summary>
        /// Process pending treasury transactions
        /// </summary>
        [HttpPost("process-pending")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ProcessPendingTransactions(
            CancellationToken cancellationToken)
        {
            try
            {
                var processedCount = await _treasuryService.ProcessPendingTransactionsAsync(
                    cancellationToken);

                return Ok(new
                {
                    message = $"Successfully processed {processedCount} pending transactions",
                    processedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pending transactions");
                return StatusCode(500, "An error occurred while processing pending transactions");
            }
        }

        /// <summary>
        /// Reverse a treasury transaction
        /// </summary>
        [HttpPost("transactions/{transactionId}/reverse")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ReverseTransaction(
            Guid transactionId,
            [FromBody] ReverseTransactionRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return BadRequest("Reason for reversal is required");
                }

                if (request.Reason.Length < 10)
                {
                    return BadRequest("Reason must be at least 10 characters long");
                }

                var reversalTransaction = await _treasuryService.ReverseTransactionAsync(
                    transactionId,
                    request.Reason,
                    cancellationToken);

                return Ok(new
                {
                    message = "Transaction reversed successfully",
                    reversalTransaction
                });
            }
            catch (Domain.Exceptions.ResourceNotFoundException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Domain.Exceptions.ValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reversing transaction {TransactionId}", transactionId);
                return StatusCode(500, "An error occurred while reversing the transaction");
            }
        }

        /// <summary>
        /// Refresh USD values for all treasury balances
        /// </summary>
        [HttpPost("refresh-usd-values")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RefreshUsdValues(CancellationToken cancellationToken)
        {
            try
            {
                await _treasuryBalanceService.RefreshUsdValuesAsync(cancellationToken);
                return Ok(new { message = "USD values refreshed successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing USD values");
                return StatusCode(500, "An error occurred while refreshing USD values");
            }
        }
    }

    #region Request Models

    public class MarkReportedRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string ReportingPeriod { get; set; } = string.Empty;
    }

    public class ReverseTransactionRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    #endregion
}
