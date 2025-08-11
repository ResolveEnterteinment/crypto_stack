using Application.Interfaces.Payment;
using Domain.Constants.Payment;
using Domain.Interfaces;
using Domain.Models.Payment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace WebApi.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/payments")]
    [Authorize(Roles = "ADMIN")]
    public class AdminPaymentController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ILogger<AdminPaymentController> _logger;

        public AdminPaymentController(
            IPaymentService paymentService,
            ILogger<AdminPaymentController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets payments with filtering and pagination
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPayments(
            [FromQuery] string status = "ALL",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            [FromQuery] string search = "")
        {
            try
            {
                // Build filter
                var filterBuilder = Builders<PaymentData>.Filter;
                var filters = new List<FilterDefinition<PaymentData>>();

                // Status filter
                if (!string.IsNullOrEmpty(status) && status != "ALL")
                {
                    filters.Add(filterBuilder.Eq(p => p.Status, status));
                }

                // Search filter - search in payment provider ID, user ID, subscription ID
                if (!string.IsNullOrEmpty(search))
                {
                    var searchFilter = filterBuilder.Or(
                        filterBuilder.Regex(p => p.PaymentProviderId, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                        filterBuilder.Regex(p => p.UserId.ToString(), new MongoDB.Bson.BsonRegularExpression(search, "i")),
                        filterBuilder.Regex(p => p.SubscriptionId.ToString(), new MongoDB.Bson.BsonRegularExpression(search, "i"))
                    );
                    filters.Add(searchFilter);
                }

                // Combine filters
                var filter = filters.Count > 0
                    ? filterBuilder.And(filters)
                    : filterBuilder.Empty;

                var sort = new SortDefinitionBuilder<PaymentData>().Ascending(p => p.CreatedAt);

                // Get paginated results
                var paymentsResult = await _paymentService.GetPaginatedAsync(
                    filter,
                    sort,
                    page,
                    pageSize);

                if (!paymentsResult.IsSuccess)
                {
                    return StatusCode(500, paymentsResult.ErrorMessage);
                }

                return Ok(new { data = paymentsResult.Data });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting admin payments");
                return StatusCode(500, "An error occurred while processing your request");
            }
        }

        /// <summary>
        /// Gets payment statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetPaymentStats()
        {
            try
            {
                var stats = new
                {
                    TotalCount = await _paymentService.Repository.CountAsync(
                        Builders<PaymentData>.Filter.Empty),
                    FailedCount = await _paymentService.Repository.CountAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Failed)),
                    PendingCount = await _paymentService.Repository.CountAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Pending)),
                    SuccessCount = await _paymentService.Repository.CountAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.Status, PaymentStatus.Filled))
                };

                return Ok(new { data = stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting payment stats");
                return StatusCode(500, "An error occurred while processing your request");
            }
        }
    }
}