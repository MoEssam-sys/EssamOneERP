using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using One.Shared;

namespace One.MAUI.Services.SignalRHub
{
    /// <summary>
    /// SignalR MainHub client for real-time messaging.
    /// Injected as a singleton.
    /// - استخدم StartAsync() مرة واحدة عند بدء التطبيق.
    /// - سجل هاندلر استقبال الرسائل عبر RegisterMessageHandler.
    /// - استخدم SendMessageAsync لإرسال رسالة إلى السيرفر.
    /// - يتعامل مع إعادة الاتصال تلقائيًا ويبلغ عن أي أخطاء اتصال.
    /// </summary>
    public class MainHub
    {
        private readonly IConfiguration _config;
        private readonly HubConnection _hubConnection;
        private bool _isStarted = false;

        public MainHub(IConfiguration config)
        {
            _config = config;
            var hubUrl = _config["SignalRHubUrl"];
            if (string.IsNullOrWhiteSpace(hubUrl))
            {
                Console.WriteLine("[SignalR] SignalRHubUrl not found in configuration. SignalR will not start.");
                return; // أو خلي الفئة تعمل Disabled Mode
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // لو حصل غلق الاتصال لأي سبب
            _hubConnection.Closed += async (error) =>
            {
                // ممكن تسجل الخطأ هنا أو تعرض رسالة للمستخدم
                Console.WriteLine("SignalR connection closed. Retrying in 5 seconds...");
                await Task.Delay(3000);
                try
                {
                    await _hubConnection.StartAsync();
                }
                catch (Exception ex)
                {
#if DEBUG
                    Console.WriteLine($"Failed to reconnect SignalR: {ex.Message}");
#endif
                    // تقدر هنا تعيد المحاولة أو تعرض للمستخدم إخطار حسب احتياجك
                }
            };

            // عند إعادة الاتصال بنجاح
            _hubConnection.Reconnected += connectionId =>
            {
#if DEBUG
                Console.WriteLine("SignalR reconnected successfully!");
#endif
                return Task.CompletedTask;
            };

            // عند محاولة إعادة الاتصال
            _hubConnection.Reconnecting += error =>
            {
#if DEBUG
                Console.WriteLine("SignalR is reconnecting...");
#endif
                return Task.CompletedTask;
            };
        }

        /// <summary>
        /// يبدأ الاتصال بالـ SignalR Hub (ننفذها مرة واحدة فقط عادة عند بدء التطبيق أو الصفحة).
        /// </summary>
        public async Task StartAsync()
        {
            if (_isStarted) return;

            try
            {
                await _hubConnection.StartAsync();
                _isStarted = true;
                Console.WriteLine("✅ SignalR connection started.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to start SignalR: {ex.Message}");
                // ممكن تعمل Retry أو تخزن الحالة عشان تحاول لاحقًا
            }
        }

        /// <summary>
        /// اغلق الاتصال يدويًا لو حبيت (اختياري)
        /// </summary>
        public async Task StopAsync()
        {
            if (_isStarted)
            {
                await _hubConnection.StopAsync();
                _isStarted = false;
            }
        }
    }
}