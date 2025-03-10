using Application.Contracts.Requests.Auth;
using Application.Contracts.Responses.Auth;
using Application.Interfaces;
using Domain.Models.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/v1/authenticate")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IAuthenticationService _authenticationService;
        private readonly RoleManager<ApplicationRole> _roleManager;

        public AuthenticationController(RoleManager<ApplicationRole> roleManager, IAuthenticationService authenticationService)
        {
            _roleManager = roleManager;
            _authenticationService = authenticationService;
        }

        [HttpPost]
        [Route("roles")]
        //[Authorize("ADMIN")]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest request)
        {
            var appRole = new ApplicationRole { Name = request.Role };
            var createRoleResult = await _roleManager.CreateAsync(appRole);

            return Ok(new { message = "role created succesfully" });
        }

        [HttpPost]
        [Route("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            var result = await _authenticationService.RegisterAsync(request);

            return Ok(result);
        }

        [HttpPost("login")]
        [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LoginResponse))]
        [ProducesResponseType((int)HttpStatusCode.Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var result = await _authenticationService.LoginAsync(request);

            if (!result.Success)
                return Unauthorized(result.Message);

            return Ok(result);
        }


        [HttpGet]
        [Route("confirm-email")]
        public async Task<IActionResult> ConfirmEmail([FromQuery] string userId, [FromQuery] string token)
        {
            var result = await _authenticationService.ConfirmEmail(userId, token);

            if (result.Success)
            {
                return Redirect(result.Message);
            }
            else
            {
                return NotFound("Confirmation Failed");
            }
        }
    }
}
