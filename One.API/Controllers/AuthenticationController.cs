using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Domain.DTOs.Identity;
using One.Domain.Localization;
using One.Infrastructure.DbContext.MainDbContext;
using One.Infrastructure.Interfaces;

namespace One.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController(IAuthService authService, ITranslator loc) : ControllerBase
    {
        //private readonly IDapperConnectionFactory _dbFactory;

        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserDto registerUser, [FromQuery] string role)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await authService.RegisterAsync(registerUser, role);

            if (result.StatusCode == StatusCodes.Status400BadRequest)
                return BadRequest(new { success = false, message = result.Message, messages = result.Messages });

            if (result.StatusCode == StatusCodes.Status401Unauthorized)
                return Unauthorized(new { success = false, message = result.Message });

            return Ok(new { success = true, message = loc["RegisteredSuccessfully"], data = result });
        }

        [HttpPost("Login")]
        public async Task<IActionResult> Login([FromBody] LoginDto login)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await authService.LoginAsync(login);

            if (result.StatusCode == StatusCodes.Status400BadRequest)
                return BadRequest(new { success = false, message = result.Message });

            if (result.StatusCode == StatusCodes.Status401Unauthorized)
                return Unauthorized(new { success = false, message = result.Message });

            return Ok(new { success = true, message = "Login successful", data = result });
        }

        [Authorize]
        [HttpGet("GetCurrentUser")]
        public async Task<IActionResult> GetCurrentUser()
        {
            var user = await authService.GetCurrentUserAsync(User);
            if (user == null || string.IsNullOrWhiteSpace(user.FirstName))
                return Unauthorized(new { message = "User not found or invalid token" });

            return Ok(user);
        }

        /// <summary>
        /// Triggers OTP generation and email for Super Admin login
        /// </summary>
        [HttpPost("Login/Otp")]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { success = false, message = "Email is required" });

            var result = await authService.LoginWithOtpAsync(request.Email);

            if (!result.Success)
            {
                // Return 500 or 400 based on failure
                // Since user wants clear error if email fails:
                return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, message = result.Messages?.FirstOrDefault() ?? "Failed to send OTP" });
            }

            return Ok(new { success = true, message = result.Data });
        }

        /// <summary>
        /// Verifies OTP and returns JWT token for Super Admin
        /// </summary>
        [HttpPost("Login/Verify")]
        public async Task<IActionResult> VerifyOtp([FromBody] OtpVerifyDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Otp))
                return BadRequest(new { success = false, message = "Email and OTP are required" });

            var result = await authService.VerifyOtpAsync(request.Email, request.Otp);

            if (result.StatusCode == StatusCodes.Status400BadRequest)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new { success = true, message = "Login successful", data = result });
        }

        /// <summary>
        /// Validates a company code for onboarding flow
        /// </summary>
        [HttpGet("ValidateCompany/{code}")]
        public async Task<IActionResult> ValidateCompany(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { success = false, message = "Company code is required" });

            var result = await authService.ValidateCompanyAsync(code);

            if (!result.Success)
                return BadRequest(new { success = false, message = result.Messages?.FirstOrDefault() });

            return Ok(new { success = true, data = result.Data });
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("Companies/Summary")]
        public async Task<IActionResult> GetCompanySummaries()
        {
            var result = await authService.GetCompanySummariesAsync();
            if (!result.Success)
                 return BadRequest(new { success = false, message = result.Messages?.FirstOrDefault() });

            return Ok(new { success = true, data = result.Data });
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("Companies/{id}")]
        public async Task<IActionResult> GetCompanyDetails(int id)
        {
             var result = await authService.GetCompanyDetailsAsync(id);
            if (!result.Success)
                 return NotFound(new { success = false, message = result.Messages?.FirstOrDefault() });

            return Ok(new { success = true, data = result.Data });
        }

    }
}
