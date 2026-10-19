using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Domain.Base;
using One.Domain.DTOs.Identity;
using One.Infrastructure.Interfaces;

namespace One.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CompaniesController(IAuthService authService, IAuthRepository authRepo) : BaseApiController
    {
        [HttpGet("Validate/{code}")]
        public async Task<IActionResult> Validate(string code)
        {
            var result = await authService.ValidateCompanyAsync(code);
            if (!result.Success)
                return NotFound(new { message = result.Messages.FirstOrDefault() });

            return Ok(result);
        }

#if DEBUG
        [Authorize(Roles = "SuperAdmin")]
        [HttpGet("List")]
        public async Task<IActionResult> List()
        {
            var list = await authRepo.GetAllCompaniesAsync();
            return Ok(Result<object>.Ok(list));
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost("Create")]
        public async Task<IActionResult> Create([FromBody] CreateCompanyRequest dto)
        {
            // Simple create
            // Note: This endpoint is query based for simplicity in debug, or body?
            // User requested "Screen", so parameters are likely form/json.
            // Using query params for debug simplicity.
            try
            {
               var company = await authRepo.CreateCompanyAsync(dto.Name, dto.Code);
               return Ok(Result<object>.Ok(company));
            }
            catch (Exception ex)
            {
                // Log internally; do not expose ex.Message to the client
                return BadRequest(Result<object>.Fail(new List<string?> { "Failed to create company." }));
            }
        }
#endif

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost("{id}/toggle-status")]
        public async Task<IActionResult> ToggleStatus(int id, [FromQuery] bool isActive)
        {
            var result = await authService.ToggleCompanyStatusAsync(id, isActive);
            if (!result.Success)
                 return BadRequest(new { success = false, message = result.Messages?.FirstOrDefault() });

            return Ok(new { success = true, data = result.Data });
        }
    }
}
