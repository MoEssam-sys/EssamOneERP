using One.Domain.Enums;
using One.Domain.DTOs.Logging;

namespace One.Shared.Services.Logging
{
    public interface IClientLogger
    {
        Task LogInfo(string message, string? action = null, string? route = null);
        Task LogWarning(string message, string? action = null, string? route = null);
        Task LogError(string message, string? action = null, string? route = null, Exception? ex = null);
        Task LogCritical(string message, string? action = null, string? route = null, Exception? ex = null);

        Task LogEvaluation(LogDto logDto); // Low level
    }
}
