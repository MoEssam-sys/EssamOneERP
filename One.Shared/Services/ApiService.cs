using One.Domain.Base;
using System.Net.Http.Json;
using System.Text.Json;

namespace One.Shared.Services
{
    public static class ApiService
    {
        public static string? AuthToken { get; set; }

        /// <summary>
        /// Global callback for handling 401 Unauthorized responses (e.g. redirect to login)
        /// </summary>
        public static Func<Task>? OnUnauthorizedAsync { get; set; }

        //var result = await ApiService.ExecuteApi<ProductDto>(
        //    http: _http,                              // ← الـ HttpClient المستخدم للاتصال بالـ API
        //    endpoint: "api/Products/Add",             // ← عنوان أو مسار الـ API المطلوب
        //    data: myProductDto,                       // ← الداتا (موديل أو object) اللي هتتبعت في البودي
        //    msgCenter: _msg,                          // ← MessagesCenter لإظهار رسائل النجاح أو الخطأ
        //    successMessage: "تمت الإضافة بنجاح!",     // ← الرسالة اللي هتظهر للمستخدم عند نجاح العملية (اختياري)
        //    onSuccess: async data => {                // ← أكشن إضافي عند النجاح (مثلاً: تحديث جدول، إعادة توجيه) (اختياري)
        //        // هنا تقدر تستخدم data (القيمة الراجعة من الـ API)
        //    },
        //    onFail: async errors => {                 // ← أكشن إضافي عند الفشل (ممكن تستخدم رسائل الخطأ) (اختياري)
        //        // errors هو List<string?> فيها رسائل الخطأ
        //    },
        //    setIsLoading: v => IsLoading = v          // ← دالة لضبط قيمة IsLoading عند بدء الطلب (true)، وإنت بنفسك هتعمل false بعد انتهاء العملية
        //    // (لو عندك أكتر من loader ممكن تمرر دالة تانية حسب مكان الفيلد)
        //);

        //    IsLoading = false; // ← بعد انتهاء العملية أو في finally (مش مسؤولية الهيلبر يرجعها false)
        public static async Task<Result<TData>?> ExecuteApi<TData>(
        HttpClient http,
        string endpoint,
        object? data,
        MessagesCenter msgCenter,
        string? successMessage = null,
        HttpMethod? method = null,
        JsonSerializerOptions? jsonOptions = null,
        Func<TData?, Task>? onSuccess = null,
        Func<List<string?>?, Task>? onFail = null,
        Action<bool>? setIsLoading = null,
        CancellationToken cancellationToken = default
    )
        {
            try
            {
                setIsLoading?.Invoke(true);

                // Create Request Message
                var request = new HttpRequestMessage(method ?? HttpMethod.Post, endpoint);

                // Add Auth Header if Token exists
                if (!string.IsNullOrWhiteSpace(AuthToken))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthToken);

                // Add Content if needed
                if (data != null && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put))
                {
                    request.Content = JsonContent.Create(data, null, jsonOptions);
                }

                HttpResponseMessage response = await http.SendAsync(request, cancellationToken);


                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                         if (OnUnauthorizedAsync != null)
                             await OnUnauthorizedAsync.Invoke();
                    }

                    var errorMsg = $"API Error ({(int)response.StatusCode}): {response.ReasonPhrase}";

                    // Try to read custom error message from body
                    try
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            using var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                                {
                                    errorMsg = msgProp.GetString() ?? errorMsg;
                                }
                                else if (doc.RootElement.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                                {
                                    errorMsg = titleProp.GetString() ?? errorMsg;
                                }
                            }
                        }
                    }
                    catch { /* Ignore parsing errors, verify generic message */ }

                    await msgCenter.PublishErrors(errorMsg);
                    if (onFail != null) await onFail(new List<string?> { errorMsg });
                    return Result<TData>.Fail(new List<string?> { errorMsg }, (int)response.StatusCode);
                }

                var result = await response.Content.ReadFromJsonAsync<Result<TData>>(jsonOptions, cancellationToken);

                if (result == null)
                {
                    string msg = "لم يتم استلام رد من الخادم.";
                    await msgCenter.PublishErrors(msg);
                    if (onFail != null) await onFail(new List<string?> { msg });
                    return Result<TData>.Fail(new List<string?> { msg });
                }

                if (result.Success)
                {
                    if (!string.IsNullOrEmpty(successMessage))
                        await msgCenter.NotifySuccess(successMessage);
                    if (onSuccess != null) await onSuccess(result.Data);
                    return result;
                }
                else
                {
                    if (result.Messages != null && result.Messages.Count > 0)
                    {
                        foreach (var err in result.Messages)
                            await msgCenter.PublishErrors(err ?? "Error");
                        if (onFail != null) await onFail(result.Messages);
                    }
                    else
                    {
                        string msg = "حدث خطأ غير محدد.";
                        await msgCenter.PublishErrors(msg);
                        if (onFail != null) await onFail(new List<string?> { msg });
                    }
                    return result;
                }
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            {
                string msg = "لا يوجد اتصال بالإنترنت. برجاء التأكد من الاتصال وحاول مرة أخرى.";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(new List<string?> { msg });
                return Result<TData>.Fail(new List<string?> { msg });
            }
            catch (TaskCanceledException)
            {
                string msg = "انتهت مهلة الاتصال بالخادم. حاول مرة أخرى لاحقًا.";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(new List<string?> { msg });
                return Result<TData>.Fail(new List<string?> { msg });
            }
            catch (Exception ex)
            {
                string msg = $"Exception: {ex.Message}";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(new List<string?> { msg });
                return Result<TData>.InternalError(ex, "حدث خطأ غير متوقع أثناء الاتصال بالخادم.");
            }
            finally
            {
                setIsLoading?.Invoke(false);
            }
        }

        //    var weather = await ApiSafeHelper.ExecuteSimpleApiWithMessage<WeatherResult>(
        //    http: _http,                                 // ← الـ HttpClient المستخدم للاتصال بالـ API
        //    endpoint: "https://api.weatherapi.com/...",  // ← عنوان أو مسار الـ API الخارجي
        //    data: null,                                  // ← الداتا للـ request (غالبًا null في GET)
        //    msgCenter: _msg,                             // ← MessagesCenter لإظهار رسائل النجاح أو الخطأ
        //    successMessage: "تم جلب الطقس!",             // ← رسالة تظهر عند نجاح العملية (اختياري)
        //    method: HttpMethod.Get,                      // ← نوع الـ HTTP method (POST/GET/PUT/DELETE) (افتراضي POST لو مش محدد)
        //    onSuccess: async data =>
        //    {                   // ← أكشن إضافي عند النجاح (data هو WeatherResult اللي راجع)
        //        // أي عمليات إضافية بالبيانات
        //    },
        //    onFail: async errMsg =>
        //    {                    // ← أكشن إضافي عند الفشل (errMsg هي رسالة نصية واحدة)
        //        // معالجة إضافية للخطأ
        //    },
        //    setIsLoading: v => IsLoading = v             // ← دالة لضبط الـ Loading
        //);
        //        IsLoading = false;
        public static async Task<TData?> ExecuteSimpleApi<TData>(
        HttpClient http,
        string endpoint,
        object? data,
        MessagesCenter msgCenter,
        string? successMessage = null,
        HttpMethod? method = null,
        JsonSerializerOptions? jsonOptions = null,
        Func<TData?, Task>? onSuccess = null,
        Func<string?, Task>? onFail = null,
        Action<bool>? setIsLoading = null,
        CancellationToken cancellationToken = default
    )
        {
            try
            {
                setIsLoading?.Invoke(true);

                // Create Request
                var request = new HttpRequestMessage(method ?? HttpMethod.Post, endpoint);

                // Add Auth Header
                if (!string.IsNullOrWhiteSpace(AuthToken))
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthToken);

                // Add Content
                if (data != null && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put))
                {
                    request.Content = JsonContent.Create(data, null, jsonOptions);
                }

                HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                         if (OnUnauthorizedAsync != null)
                             await OnUnauthorizedAsync.Invoke();
                    }

                    var errorMsg = $"API Error ({(int)response.StatusCode}): {response.ReasonPhrase}";

                    // Try to read custom error message from body
                    try
                    {
                        var content = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            using var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                            {
                                if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                                {
                                    errorMsg = msgProp.GetString() ?? errorMsg;
                                }
                                else if (doc.RootElement.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                                {
                                    errorMsg = titleProp.GetString() ?? errorMsg;
                                }
                                // Handle { success: false, message: "..." } structure specifically if needed,
                                // but the above 'message' check usually covers it.
                            }
                        }
                    }
                    catch { /* Ignore parsing errors, verify generic message */ }

                    await msgCenter.PublishErrors(errorMsg);
                    if (onFail != null) await onFail(errorMsg);
                    return default;
                }

                var result = await response.Content.ReadFromJsonAsync<TData>(jsonOptions, cancellationToken);

                if (result == null)
                {
                    string msg = "لم يتم استلام بيانات من الخادم.";
                    await msgCenter.PublishErrors(msg);
                    if (onFail != null) await onFail(msg);
                    return default;
                }

                if (!string.IsNullOrEmpty(successMessage))
                    await msgCenter.NotifySuccess(successMessage);

                if (onSuccess != null) await onSuccess(result);

                return result;
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException se && se.SocketErrorCode == System.Net.Sockets.SocketError.HostNotFound)
            {
                string msg = "لا يوجد اتصال بالإنترنت. برجاء التأكد من الاتصال وحاول مرة أخرى.";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(msg);
                return default;
            }
            catch (TaskCanceledException)
            {
                string msg = "انتهت مهلة الاتصال بالخادم. حاول مرة أخرى لاحقًا.";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(msg);
                return default;
            }
            catch (Exception ex)
            {
                string msg = $"Exception: {ex.Message}";
                await msgCenter.PublishErrors(msg);
                if (onFail != null) await onFail(msg);
                return default;
            }
            finally
            {
                setIsLoading?.Invoke(false);
            }
        }
    }
}