using One.Domain.Enums;

namespace One.Domain.DTOs.Logging
{
    public class LogDto
    {
        public LogSeverity Severity { get; set; }
        public LogCategory Category { get; set; }

        public string? Route { get; set; }
        public string? Action { get; set; }
        public string Message { get; set; } = string.Empty;

        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public string? Hash { get; set; }

        public string? Platform { get; set; }
        public string? DeviceModel { get; set; }
        public string? AppVersion { get; set; }

        public string? MetadataJson { get; set; }

        // Client timestamp can be useful for offline sync ordering
        public DateTime ClientTimestampUtc { get; set; }
    }
}
