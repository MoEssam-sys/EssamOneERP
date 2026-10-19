using System.ComponentModel.DataAnnotations;
using One.Domain.Enums;

namespace One.Domain.Entities.Identity
{
    public class AppLog
    {
        [Key]
        public int Id { get; set; }

        public LogSeverity Severity { get; set; }
        public LogCategory Category { get; set; }

        public string? UserId { get; set; }
        public string? UserName { get; set; }
        public int? CompanyId { get; set; }

        public string? Route { get; set; }
        public string? Action { get; set; }

        [Required]
        public string Message { get; set; } = string.Empty;

        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public string? Hash { get; set; } // For deduplication/grouping

        public string? Platform { get; set; } // Android, iOS, Windows
        public string? DeviceModel { get; set; }
        public string? AppVersion { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
