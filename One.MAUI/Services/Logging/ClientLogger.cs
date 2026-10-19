using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Devices;
using One.Domain.DTOs.Logging;
using One.Domain.Enums;
using One.Shared.Services.Logging;

namespace One.MAUI.Services.Logging
{
    public class ClientLogger : IClientLogger, IDisposable
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConcurrentDictionary<string, DateTime> _dedupCache = new();
        private readonly TimeSpan _dedupWindow = TimeSpan.FromSeconds(3); // 3 seconds window
        private readonly SemaphoreSlim _lock = new(1, 1);
        private const string OfflineQueueKey = "OffflineLogsQueue";
        private Timer? _flushTimer;

        public ClientLogger(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            // Auto flush every 60 seconds
            _flushTimer = new Timer(async _ => await FlushQueueAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60));
        }

        private HttpClient CreateClient() => _httpClientFactory.CreateClient("ApiClient");

        public async Task LogInfo(string message, string? action = null, string? route = null)
        {
            await LogEvaluation(new LogDto
            {
                Severity = LogSeverity.Info,
                Category = LogCategory.Activity,
                Message = message,
                Action = action,
                Route = route
            });
        }

        public async Task LogWarning(string message, string? action = null, string? route = null)
        {
            await LogEvaluation(new LogDto
            {
                Severity = LogSeverity.Warning,
                Category = LogCategory.General,
                Message = message,
                Action = action,
                Route = route
            });
        }

        public async Task LogError(string message, string? action = null, string? route = null, Exception? ex = null)
        {
             await LogEvaluation(new LogDto
            {
                Severity = LogSeverity.Error,
                Category = ex != null ? LogCategory.Exception : LogCategory.UserError,
                Message = message,
                Action = action,
                Route = route,
                ExceptionType = ex?.GetType().Name,
                StackTrace = ex?.ToString()
            });
        }

        public async Task LogCritical(string message, string? action = null, string? route = null, Exception? ex = null)
        {
            await LogEvaluation(new LogDto
            {
                Severity = LogSeverity.Critical,
                Category = LogCategory.System,
                Message = message,
                Action = action,
                Route = route,
                ExceptionType = ex?.GetType().Name,
                StackTrace = ex?.ToString()
            });
        }

        public async Task LogEvaluation(LogDto log)
        {
            try
            {
                // 1. Enrich
                EnrichLog(log);

                // 2. Dedup
                if (log.Severity >= LogSeverity.Error && IsDuplicate(log))
                {
                    return;
                }

                // 3. Try Send
                bool sent = await SendToApi(log);

                // 4. Offline Queue if failed
                if (!sent)
                {
                    await EnqueueOffline(log);
                }
            }
            catch (Exception ex)
            {
                // Fallback debug
                System.Diagnostics.Debug.WriteLine($"[ClientLogger] Critical failure: {ex}");
            }
        }

        private void EnrichLog(LogDto log)
        {
            log.ClientTimestampUtc = DateTime.UtcNow;

            // Device Info (MAUI specific)
            try
            {
                 log.Platform = DeviceInfo.Platform.ToString();
                 log.DeviceModel = DeviceInfo.Model;
                 log.AppVersion = AppInfo.VersionString;
            }
            catch
            {
                log.Platform = "Unknown";
            }

            // Calculate Hash if not present
            if (string.IsNullOrEmpty(log.Hash))
            {
                var input = $"{log.Severity}|{log.Category}|{log.Message}|{log.StackTrace}|{log.Route}";
                log.Hash = ComputeSha256(input);
            }
        }

        private bool IsDuplicate(LogDto log)
        {
            if (string.IsNullOrEmpty(log.Hash)) return false;

            var now = DateTime.UtcNow;
            if (_dedupCache.TryGetValue(log.Hash, out var lastSeen))
            {
                if ((now - lastSeen) < _dedupWindow)
                {
                    return true; // Suppress
                }
            }

            _dedupCache[log.Hash] = now;

            // Cleanup cache periodically?
            // Dictionary size logic could be added here to prevent memory leak if many unique errors.
            if (_dedupCache.Count > 100) _dedupCache.Clear();

            return false;
        }

        private async Task<bool> SendToApi(LogDto log)
        {
            // If offline, don't even try, go straight to queue
            if (Connectivity.NetworkAccess != NetworkAccess.Internet) return false;

            try
            {
                var client = CreateClient();
                var response = await client.PostAsJsonAsync("api/Logs", log);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task EnqueueOffline(LogDto log)
        {
            await _lock.WaitAsync();
            try
            {
                var pendingJson = Preferences.Get(OfflineQueueKey, "[]");
                var pending = JsonSerializer.Deserialize<List<LogDto>>(pendingJson) ?? new List<LogDto>();

                pending.Add(log);

                // Limit queue size
                if (pending.Count > 50) pending.RemoveAt(0);

                var newJson = JsonSerializer.Serialize(pending);
                Preferences.Set(OfflineQueueKey, newJson);
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"[ClientLogger] Save queue failed: {ex}");
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task FlushQueueAsync()
        {
            if (Connectivity.NetworkAccess != NetworkAccess.Internet) return;

            string pendingJson = Preferences.Get(OfflineQueueKey, "[]");
            if (pendingJson == "[]") return;

            await _lock.WaitAsync();
            try
            {
                // Re-read inside lock
                pendingJson = Preferences.Get(OfflineQueueKey, "[]");
                var pending = JsonSerializer.Deserialize<List<LogDto>>(pendingJson);

                if (pending == null || pending.Count == 0) return;

                // Send batch
                try
                {
                    var client = CreateClient();
                    var response = await client.PostAsJsonAsync("api/Logs/batch", pending);
                    if (response.IsSuccessStatusCode)
                    {
                        // Clear queue
                        Preferences.Set(OfflineQueueKey, "[]");
                        System.Diagnostics.Debug.WriteLine($"[ClientLogger] Flushed {pending.Count} logs.");
                    }
                }
                catch
                {
                    // Keep for next time
                }
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"[ClientLogger] Flush failed: {ex}");
            }
            finally
            {
                _lock.Release();
            }
        }

        private static string ComputeSha256(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }

        public void Dispose()
        {
            _flushTimer?.Dispose();
        }
    }
}
