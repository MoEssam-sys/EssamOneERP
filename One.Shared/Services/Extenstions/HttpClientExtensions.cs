using One.Domain.Base;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace One.Shared.Services.Extenstions
{
    public static class HttpClientExtensions
    {
        // ✅ UI context بتاع Blazor renderer (يتعمله capture من Routes بعد أول Render)
        private static SynchronizationContext? _uiCtx;

        public static void CaptureUiContext()
        {
            _uiCtx ??= SynchronizationContext.Current;
        }

        private static readonly JsonSerializerOptions _json =
            new()
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

#if DEBUG
        private const bool IsDebugBuild = true;
#else
        private const bool IsDebugBuild = false;
#endif

        private static bool DebugEnabled => IsDebugBuild || Debugger.IsAttached;

        // ===================== Core (Any HttpMethod) =====================
        public static async Task<Result<TData>> SendResultAsync<TData>(
        this HttpClient http,
        HttpMethod method,
        string relativeApi,
        object? body = null,
        string? onError = null,
        MessagesCenter? ui = null,
        CancellationToken ct = default,
        [CallerFilePath] string? callerFile = null,
        [CallerMemberName] string? callerMember = null,
        [CallerLineNumber] int callerLine = 0)
        {
            if (Uri.TryCreate(relativeApi, UriKind.Absolute, out _))
                throw new InvalidOperationException("Only relative API paths are permitted.");

            try
            {
                using var req = new HttpRequestMessage(method, relativeApi);

                if (body is not null)
                    req.Content = JsonContent.Create(body, options: _json);

                using var resp = await http.SendAsync(req, ct);

                // 401/403
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    var (msgs401, raw401, _) = await ReadAndParseErrorsAsync(
                        resp.Content,
                        onError ?? "You are not authorized.",
                        ct);

                    var (dbg401, meta401) = BuildDebugHttpDetails(http, method, relativeApi, resp, raw401, callerFile, callerMember, callerLine);

                    await PublishMessagesAsync(ui, success: false, isWarning: false, msgs401, onError, debugDetails: dbg401, debugMeta: meta401);

                    return new Result<TData>
                    {
                        Success = false,
                        Messages = msgs401,
                        StatusCode = (int)resp.StatusCode
                    };
                }

                if (resp.IsSuccessStatusCode)
                {
                    var raw = await resp.Content.ReadAsStringAsync(ct);

                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return new Result<TData>
                        {
                            Success = true,
                            Data = default,
                            StatusCode = (int)resp.StatusCode
                        };
                    }

                    var wrapped = TryDeserialize<Result<TData>>(raw);
                    if (wrapped is not null)
                    {
                        await PublishMessagesAsync(ui, wrapped.Success, wrapped.IsWarning, wrapped.Messages, onError);

                        if (wrapped.Success)
                            return wrapped;

                        var (dbg, meta) = BuildDebugHttpDetails(http, method, relativeApi, resp, raw, callerFile, callerMember, callerLine);
                        await PublishMessagesAsync(ui, success: false, isWarning: wrapped.IsWarning, wrapped.Messages, onError, debugDetails: dbg, debugMeta: meta);

                        return new Result<TData>
                        {
                            Success = false,
                            Messages = wrapped.Messages ?? new List<string?> { onError ?? "Operation failed." },
                            StatusCode = wrapped.StatusCode ?? (int)resp.StatusCode,
                            ErrorId = wrapped.ErrorId,
                            Metadata = wrapped.Metadata,
                            IsWarning = wrapped.IsWarning
                        };
                    }

                    var data = TryDeserialize<TData>(raw);
                    if (data is not null)
                    {
                        return new Result<TData>
                        {
                            Success = true,
                            Data = data,
                            StatusCode = (int)resp.StatusCode
                        };
                    }

                    // هنا ممكن تكون ValidationProblemDetails حتى لو status 2xx (نادر) بس نخليها مرنة
                    var (msgs, isValidationFromRaw) = ParseErrorMessagesFromRaw(raw, onError);
                    var (dbgFail, metaFail) = BuildDebugHttpDetails(http, method, relativeApi, resp, raw, callerFile, callerMember, callerLine);
                    await PublishMessagesAsync(ui, success: false, isWarning: isValidationFromRaw, msgs, onError, debugDetails: dbgFail, debugMeta: metaFail);

                    return new Result<TData>
                    {
                        Success = false,
                        Messages = msgs,
                        StatusCode = (int)resp.StatusCode
                    };
                }
                else
                {
                    var (msgs, raw, isValidation) = await ReadAndParseErrorsAsync(resp.Content, onError, ct);
                    var (dbg, meta) = BuildDebugHttpDetails(http, method, relativeApi, resp, raw, callerFile, callerMember, callerLine);

                    await PublishMessagesAsync(ui, success: false, isWarning: isValidation, msgs, onError, debugDetails: dbg, debugMeta: meta);

                    return new Result<TData>
                    {
                        Success = false,
                        Messages = msgs,
                        StatusCode = (int)resp.StatusCode
                    };
                }
            }
            catch (TaskCanceledException tex) when (!ct.IsCancellationRequested)
            {
                var msgs = new List<string?> { "The request timed out. Please try again.", onError };
                var dbg = BuildDebugExceptionDetails("Timeout", tex, callerFile, callerMember, callerLine);

                await NotifyExceptionAsync("Timeout", tex, ui);
                await PublishMessagesAsync(ui, success: false, isWarning: false, msgs, onError, dbg);

                return new Result<TData>
                {
                    Success = false,
                    Messages = msgs,
                    StatusCode = (int)HttpStatusCode.RequestTimeout
                };
            }
            catch (HttpRequestException hex)
            {
                var msgs = new List<string?> { "Network error while contacting the server.", hex.Message, onError };
                var dbg = BuildDebugExceptionDetails("HttpRequest", hex, callerFile, callerMember, callerLine);

                await NotifyExceptionAsync("HttpRequest", hex, ui);
                await PublishMessagesAsync(ui, success: false, isWarning: false, msgs, onError, dbg);

                return new Result<TData> { Success = false, Messages = msgs };
            }
            catch (JsonException jx)
            {
                var msgs = new List<string?> { "Invalid response format.", jx.Message, onError };
                var dbg = BuildDebugExceptionDetails("Json", jx, callerFile, callerMember, callerLine);

                await NotifyExceptionAsync("Json", jx, ui);
                await PublishMessagesAsync(ui, success: false, isWarning: false, msgs, onError, dbg);

                return new Result<TData> { Success = false, Messages = msgs };
            }
            catch (Exception ex)
            {
                var msgs = new List<string?> { onError ?? "Unexpected error occurred." };
                var dbg = BuildDebugExceptionDetails("Unexpected", ex, callerFile, callerMember, callerLine);

                await NotifyExceptionAsync("Unexpected", ex, ui);
                await PublishMessagesAsync(ui, success: false, isWarning: false, msgs, onError, dbg);

                return new Result<TData> { Success = false, Messages = msgs };
            }
        }

        // ===================== Simple Overloads (with Caller Info) =====================
        public static Task<Result<TData>> GetResultAsync<TData>(
            this HttpClient http,
            string relativeApi,
            string? onError = null,
            MessagesCenter? ui = null,
            CancellationToken ct = default,
            [CallerFilePath] string? callerFile = null,
            [CallerMemberName] string? callerMember = null,
            [CallerLineNumber] int callerLine = 0)
            => http.SendResultAsync<TData>(HttpMethod.Get, relativeApi, null, onError, ui, ct, callerFile, callerMember, callerLine);

        public static Task<Result<TData>> PostResultAsync<TData, TBody>(
            this HttpClient http,
            string relativeApi,
            TBody body,
            string? onError = null,
            MessagesCenter? ui = null,
            CancellationToken ct = default,
            [CallerFilePath] string? callerFile = null,
            [CallerMemberName] string? callerMember = null,
            [CallerLineNumber] int callerLine = 0)
            => http.SendResultAsync<TData>(HttpMethod.Post, relativeApi, body, onError, ui, ct, callerFile, callerMember, callerLine);

        public static Task<Result<TData>> PutResultAsync<TData, TBody>(
            this HttpClient http,
            string relativeApi,
            TBody body,
            string? onError = null,
            MessagesCenter? ui = null,
            CancellationToken ct = default,
            [CallerFilePath] string? callerFile = null,
            [CallerMemberName] string? callerMember = null,
            [CallerLineNumber] int callerLine = 0)
            => http.SendResultAsync<TData>(HttpMethod.Put, relativeApi, body, onError, ui, ct, callerFile, callerMember, callerLine);

        public static Task<Result<TData>> DeleteResultAsync<TData>(
            this HttpClient http,
            string relativeApi,
            string? onError = null,
            MessagesCenter? ui = null,
            CancellationToken ct = default,
            [CallerFilePath] string? callerFile = null,
            [CallerMemberName] string? callerMember = null,
            [CallerLineNumber] int callerLine = 0)
            => http.SendResultAsync<TData>(HttpMethod.Delete, relativeApi, null, onError, ui, ct, callerFile, callerMember, callerLine);

        // ===================== UI publish (ALWAYS on captured UI ctx) =====================
        private static async Task PublishMessagesAsync(
        MessagesCenter? ui,
        bool success,
        bool isWarning,
        List<string?>? msgs,
        string? fallback = null,
        string? debugDetails = null,
        IReadOnlyDictionary<string, string>? debugMeta = null)
        {
            if (ui is null) return;

            var cleaned = CleanMessages(msgs, success ? null : fallback);

            await RunOnUiAsync(async () =>
            {
                if (!success && DebugEnabled && !string.IsNullOrWhiteSpace(debugDetails) && cleaned.Count > 0)
                {
                    await ui.DevErrorDialogAsync(
                        title: "DEV: HTTP Error",
                        summary: fallback ?? "Request failed",
                        copyText: debugDetails,
                        chips: cleaned,
                        meta: debugMeta,
                        isValidation: isWarning
                    );
                }

                if (cleaned.Count == 0) return;

                if (!success)
                {
                    if (isWarning)
                    {
                        foreach (var m in cleaned)
                            await ui.NotifyWarning(m!);
                        return;
                    }

                    await ui.Error(cleaned);
                    return;
                }

                if (isWarning)
                {
                    foreach (var m in cleaned)
                        await ui.NotifyWarning(m!);
                }
                else
                {
                    foreach (var m in cleaned)
                        await ui.NotifyInfo(m!);
                }
            });
        }

        private static async Task NotifyExceptionAsync(string key, Exception ex, MessagesCenter? ui)
        {
            if (ui is null) return;

            await RunOnUiAsync(() => ui.ExceptionHandle(key, ex));
        }

        private static List<string?> CleanMessages(List<string?>? msgs, string? fallback)
        {
            var clean = (msgs ?? new List<string?>())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            if (clean.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
                clean.Add(fallback);

            return clean;
        }

        /// <summary>
        /// ✅ ينفّذ على UI SynchronizationContext بتاع Blazor (اللي اتعمله Capture في Routes)
        /// لو مش موجود، ينفّذ عادي.
        /// </summary>
        private static Task RunOnUiAsync(Func<Task> work)
        {
            var ctx = _uiCtx;

            // لو احنا بالفعل على نفس الـctx، نفّذ مباشر
            if (ctx is null || ReferenceEquals(SynchronizationContext.Current, ctx))
                return SafeExecute(work);

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            ctx.Post(async _ =>
            {
                try
                {
                    await SafeExecute(work);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, null);

            return tcs.Task;

            static async Task SafeExecute(Func<Task> w)
            {
                try { await w(); }
                catch (ObjectDisposedException) { /* ignore */ }
            }
        }

        // ===================== Helpers =====================
        private static async Task<Result<TData>?> SafeReadAsResultAsync<TData>(
            HttpContent content,
            CancellationToken ct)
        {
            try
            {
                return await content.ReadFromJsonAsync<Result<TData>>(_json, ct);
            }
            catch
            {
                return null;
            }
        }

        // ✅ نقرأ الـ body مرة واحدة فقط (عشان الـ stream مايتقفلش)
        private static async Task<(List<string?> Messages, string RawBody, bool IsValidation)> ReadAndParseErrorsAsync(
        HttpContent content,
        string? fallbackMessage,
        CancellationToken ct)
        {
            var raw = await content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(raw))
            {
                var msgs = new List<string?>();
                if (!string.IsNullOrWhiteSpace(fallbackMessage))
                    msgs.Add(fallbackMessage);
                return (msgs, raw, false);
            }

            var (messages, isValidation) = ParseErrorMessagesFromRaw(raw, fallbackMessage);
            return (messages, raw, isValidation);
        }

        private static (List<string?> Messages, bool IsValidation) ParseErrorMessagesFromRaw(string raw, string? fallbackMessage)
        {
            var msgs = new List<string?>();
            var hasModelStateErrors = false;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                // Result.Messages
                if (TryGetArray(root, "messages", out var arr1) || TryGetArray(root, "Messages", out arr1))
                {
                    foreach (var i in arr1!.Value.EnumerateArray())
                        msgs.Add(i.ValueKind == JsonValueKind.String ? i.GetString() : i.ToString());
                }

                // ModelState errors (ValidationProblemDetails)
                if (TryGet(root, "errors", out var errors) || TryGet(root, "Errors", out errors))
                {
                    var e = errors!.Value;

                    if (e.ValueKind == JsonValueKind.Object)
                    {
                        hasModelStateErrors = true;

                        foreach (var field in e.EnumerateObject())
                        {
                            if (field.Value.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var v in field.Value.EnumerateArray())
                                {
                                    var val = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
                                    if (!string.IsNullOrWhiteSpace(val))
                                        msgs.Add(string.IsNullOrWhiteSpace(field.Name) ? val : $"{field.Name}: {val}");
                                }
                            }
                            else
                            {
                                var val = field.Value.ToString();
                                if (!string.IsNullOrWhiteSpace(val))
                                    msgs.Add($"{field.Name}: {val}");
                            }
                        }
                    }
                    else if (e.ValueKind == JsonValueKind.Array)
                    {
                        hasModelStateErrors = true;

                        foreach (var v in e.EnumerateArray())
                            msgs.Add(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
                    }
                }

                // array of strings
                if (root.ValueKind == JsonValueKind.Array && msgs.Count == 0)
                {
                    foreach (var v in root.EnumerateArray())
                        msgs.Add(v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString());
                }

                // ProblemDetails/common keys (بس لو مش validation)
                if (root.ValueKind == JsonValueKind.Object && !hasModelStateErrors)
                {
                    foreach (var key in new[] { "message", "Message", "error", "Error", "detail", "Detail", "title", "Title" })
                    {
                        if (root.TryGetProperty(key, out var p) &&
                            p.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
                        {
                            var val = p.ToString();
                            if (!string.IsNullOrWhiteSpace(val))
                                msgs.Add(val);
                        }
                    }
                }

                if (msgs.Count == 0)
                    msgs.Add(raw);
            }
            catch
            {
                msgs.Add(raw);
            }

            msgs = msgs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string?>()
                .ToList();

            if (msgs.Count == 0 && !string.IsNullOrWhiteSpace(fallbackMessage))
                msgs.Add(fallbackMessage);

            return (msgs, hasModelStateErrors);

            static bool TryGet(JsonElement root, string name, out JsonElement? el)
            {
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var prop))
                {
                    el = prop;
                    return true;
                }
                el = null;
                return false;
            }

            static bool TryGetArray(JsonElement root, string name, out JsonElement? el)
            {
                if (TryGet(root, name, out el) && el!.Value.ValueKind == JsonValueKind.Array)
                    return true;

                el = null;
                return false;
            }
        }

        // ===================== Debug Builders =====================
        private static (string? CopyText, Dictionary<string, string> Meta) BuildDebugHttpDetails(
        HttpClient http,
        HttpMethod method,
        string relativeApi,
        HttpResponseMessage resp,
        string rawBody,
        string? callerFile,
        string? callerMember,
        int callerLine)
        {
            var meta = new Dictionary<string, string>();

            if (!DebugEnabled)
                return (null, meta);

            var url = http.BaseAddress is null
                ? relativeApi
                : new Uri(http.BaseAddress, relativeApi).ToString();

            var file = string.IsNullOrWhiteSpace(callerFile) ? "UnknownFile" : Path.GetFileName(callerFile);

            meta["Status"] = $"{(int)resp.StatusCode} ({resp.StatusCode})";
            meta["Reason"] = resp.ReasonPhrase ?? resp.StatusCode.ToString();
            meta["Request"] = $"{method} {url}";
            meta["Caller"] = $"{file}::{callerMember} (line {callerLine})";
            meta["Time"] = $"{DateTimeOffset.Now:O}";

            var sb = new StringBuilder();
            sb.AppendLine("────────── DEBUG HTTP ERROR ──────────");
            foreach (var kv in meta)
                sb.AppendLine($"{kv.Key,-8}: {kv.Value}");

            if (!string.IsNullOrWhiteSpace(rawBody))
            {
                sb.AppendLine("Body     :");
                sb.AppendLine(Indent(Truncate(rawBody, 6000)));
            }

            sb.AppendLine("──────────────────────────────────────");

            return (sb.ToString(), meta);
        }

        private static string? BuildDebugLogicalFail<TData>(
            HttpClient http,
            HttpMethod method,
            string relativeApi,
            HttpResponseMessage resp,
            Result<TData> res,
            string? callerFile,
            string? callerMember,
            int callerLine)
        {
            if (!DebugEnabled) return null;

            var url = http.BaseAddress is null
                ? relativeApi
                : new Uri(http.BaseAddress, relativeApi).ToString();

            var file = string.IsNullOrWhiteSpace(callerFile) ? "UnknownFile" : Path.GetFileName(callerFile);

            var sb = new StringBuilder();
            sb.AppendLine("────────── DEBUG LOGICAL FAIL ──────────");
            sb.AppendLine($"HTTP     : {(int)resp.StatusCode} ({resp.StatusCode})");
            sb.AppendLine($"Request  : {method} {url}");
            sb.AppendLine($"Caller   : {file}::{callerMember} (line {callerLine})");
            sb.AppendLine($"Result   : Success={res.Success}, IsWarning={res.IsWarning}, StatusCode={res.StatusCode}, ErrorId={res.ErrorId}");
            sb.AppendLine("───────────────────────────────────────");
            return sb.ToString();
        }

        private static string? BuildDebugExceptionDetails(
            string key,
            Exception ex,
            string? callerFile,
            string? callerMember,
            int callerLine)
        {
            if (!DebugEnabled) return null;

            var file = string.IsNullOrWhiteSpace(callerFile) ? "UnknownFile" : Path.GetFileName(callerFile);

            var sb = new StringBuilder();
            sb.AppendLine("────────── DEBUG EXCEPTION ──────────");
            sb.AppendLine($"Key      : {key}");
            sb.AppendLine($"Caller   : {file}::{callerMember} (line {callerLine})");
            sb.AppendLine($"Time     : {DateTimeOffset.Now:O}");
            sb.AppendLine("Exception:");
            sb.AppendLine(Indent(Truncate(ex.ToString(), 8000)));
            sb.AppendLine("─────────────────────────────────────");
            return sb.ToString();
        }

        private static string Truncate(string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "\n... (truncated)";

        private static string Indent(string s)
        {
            var lines = s.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Select(l => "  " + l));
        }

        private static T? TryDeserialize<T>(string raw)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(raw, _json);
            }
            catch
            {
                return default;
            }
        }
    }
}