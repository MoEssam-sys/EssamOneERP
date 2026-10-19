using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using One.Domain.DTOs.Logging;
using One.Domain.Entities.Identity;
using One.Infrastructure.DbContext.UsersDbContext;
using System.Security.Claims;

namespace One.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class LogsController : ControllerBase
    {
        private readonly UsersDbContext _context;

        public LogsController(UsersDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> CreateLog([FromBody] LogDto dto)
        {
            if (dto == null) return BadRequest("Log data is null");

            string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            string? userName = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
            int? companyId = int.TryParse(companyIdClaim, out int cid) ? cid : null;

            var log = new AppLog
            {
                Severity = dto.Severity,
                Category = dto.Category,

                // Security Context
                UserId = userId,
                UserName = userName,
                CompanyId = companyId,

                // Action Context
                Route = dto.Route,
                Action = dto.Action,
                Message = dto.Message,

                // Technical Context
                ExceptionType = dto.ExceptionType,
                StackTrace = dto.StackTrace,
                Hash = dto.Hash,

                // Device Context
                Platform = dto.Platform,
                DeviceModel = dto.DeviceModel,
                AppVersion = dto.AppVersion,
                MetadataJson = dto.MetadataJson,

                // Timestamp
                CreatedAtUtc = DateTime.UtcNow
            };

            await _context.AppLogs.AddAsync(log);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost("batch")]
        public async Task<IActionResult> CreateLogsBatch([FromBody] List<LogDto> dtos)
        {
            if (dtos == null || dtos.Count == 0) return Ok();

            string? userId = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
            string? userName = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
            var companyIdClaim = User.Claims.FirstOrDefault(c => c.Type == "companyId")?.Value;
            int? companyId = int.TryParse(companyIdClaim, out int cid) ? cid : null;

            var logs = new List<AppLog>();

            foreach (var dto in dtos)
            {
                logs.Add(new AppLog
                {
                    Severity = dto.Severity,
                    Category = dto.Category,
                    UserId = userId,
                    UserName = userName,
                    CompanyId = companyId,
                    Route = dto.Route,
                    Action = dto.Action,
                    Message = dto.Message,
                    ExceptionType = dto.ExceptionType,
                    StackTrace = dto.StackTrace,
                    Hash = dto.Hash,
                    Platform = dto.Platform,
                    DeviceModel = dto.DeviceModel,
                    AppVersion = dto.AppVersion,
                    MetadataJson = dto.MetadataJson,
                    CreatedAtUtc = DateTime.UtcNow // Or use client timestamp if critical
                });
            }

            await _context.AppLogs.AddRangeAsync(logs);
            await _context.SaveChangesAsync();

            return Ok();
        }
    }
}
