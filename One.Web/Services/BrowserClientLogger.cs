using One.Domain.DTOs.Logging;
using One.Domain.Enums;
using One.Shared.Services.Logging;
using System.Net.Http.Json;

namespace One.Web.Services;

public sealed class BrowserClientLogger(HttpClient http) : IClientLogger
{
    public Task LogInfo(string message, string? action = null, string? route = null) =>
        LogEvaluation(Create(LogSeverity.Info, LogCategory.Activity, message, action, route));

    public Task LogWarning(string message, string? action = null, string? route = null) =>
        LogEvaluation(Create(LogSeverity.Warning, LogCategory.General, message, action, route));

    public Task LogError(string message, string? action = null, string? route = null, Exception? ex = null) =>
        LogEvaluation(Create(LogSeverity.Error, LogCategory.UserError, message, action, route, ex));

    public Task LogCritical(string message, string? action = null, string? route = null, Exception? ex = null) =>
        LogEvaluation(Create(LogSeverity.Critical, LogCategory.System, message, action, route, ex));

    public async Task LogEvaluation(LogDto logDto)
    {
        try
        {
            logDto.Platform = "Browser";
            logDto.ClientTimestampUtc = DateTime.UtcNow;
            await http.PostAsJsonAsync("api/Logs", logDto);
        }
        catch
        {
            // Client logging must never break the UI.
        }
    }

    private static LogDto Create(
        LogSeverity severity,
        LogCategory category,
        string message,
        string? action,
        string? route,
        Exception? ex = null) => new()
        {
            Severity = severity,
            Category = category,
            Message = message,
            Action = action,
            Route = route,
            ExceptionType = ex?.GetType().Name,
            StackTrace = ex?.ToString()
        };
}
