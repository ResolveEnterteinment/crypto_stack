using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    [Produces("application/json")]
    public class CsrfController : ControllerBase
    {
        private readonly IAntiforgery _antiforgery;

        public CsrfController(IAntiforgery antiforgery)
        {
            _antiforgery = antiforgery;
        }

        [HttpGet]
        [IgnoreAntiforgeryToken]
        public IActionResult GetToken()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            return Ok(new { token = tokens.RequestToken });
        }
    }
}