using AKSoftware.Localization.MultiLanguages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using One.MAUI.Services.Platform;
using One.MAUI.Services.SignalRHub;
using One.Shared.Configuration;
using One.Shared.Services; // MessagesCenter
using One.Shared.Services.ErrorNotifier; // IGlobalErrorNotifier
using One.Shared.Services.Extenstions; // HttpClientExtensions
using One.Shared.Services.Logging;
using One.Shared.Services.Platform;
using System.Net;
using System.Net.Http;
using Polly;
using Polly.Extensions.Http;

namespace One.MAUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            AddPackagedAppSettings(builder);
            AddLocalOverrideIfExists(builder);

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif
            builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
            builder.Services.AddSingleton<MainHub>();
            builder.Services.AddOneShared();
            builder.Services.AddSingleton<IClientStorage, MauiClientStorage>();
            builder.Services.AddTransient<ApiAuthorizationHandler>();

            // ✅ HttpClient مُدار عبر IHttpClientFactory لتقليل استنزاف المقابس وتحسين الأداء عبر Windows/Android
            // ✅ Polly retry policy: handles cold-start by retrying on connection failures/timeouts
            builder.Services.AddHttpClient("ApiClient", client =>
            {
                client.BaseAddress = GetBaseAddressByPlatform();
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .AddHttpMessageHandler<ApiAuthorizationHandler>()
            .AddPolicyHandler(GetRetryPolicy());

            // ✅ Resolve a single scoped client instance from the factory
            builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("ApiClient"));

            // ✅ Logging Service
            builder.Services.AddSingleton<IClientLogger, One.MAUI.Services.Logging.ClientLogger>();

            // ✅ Global notifier (لو مش متسجل)
            builder.Services.AddSingleton<IGlobalErrorNotifier, GlobalErrorNotifier>();

            var app = builder.Build();

            // ✅ اربط Global exception handlers (Logging/Notify فقط، بدون UI معقّد)
            WireGlobalExceptionHandlers(app.Services);

            return app;
        }

        private static Uri GetBaseAddressByPlatform()
        {
#if ANDROID
            return new Uri("http://10.0.2.2:5253/");
#elif WINDOWS
            return new Uri("https://localhost:7211/");
#elif IOS
            return new Uri("https://localhost:7211/");
#else
            return new Uri("http://localhost:5253/");
#endif
        }

        private static HttpMessageHandler CreatePrimaryHandler()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                MaxConnectionsPerServer = 20
            };

#if DEBUG
            // WARNING: This bypasses SSL certificate validation and should NEVER be enabled in production!
            // This is only acceptable for local development with self-signed certificates.
            System.Diagnostics.Debug.WriteLine("[Security Warning] SSL certificate validation is disabled. This is only safe for local development.");
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif

            return handler;
        }

        private static void AddPackagedAppSettings(MauiAppBuilder builder)
        {
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json")
                                             .GetAwaiter().GetResult();
                builder.Configuration.AddJsonStream(stream);
            }
            catch (Exception ex)
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>());
                System.Diagnostics.Debug.WriteLine($"[Config] appsettings.json not found in package: {ex.Message}");
            }
        }

        private static void AddLocalOverrideIfExists(MauiAppBuilder builder)
        {
            var local = Path.Combine(FileSystem.AppDataDirectory, "appsettings.local.json");
            if (File.Exists(local))
                builder.Configuration.AddJsonFile(local, optional: true, reloadOnChange: false);
        }

        private static void WireGlobalExceptionHandlers(IServiceProvider services)
        {
            var notifier = services.GetRequiredService<IGlobalErrorNotifier>();

            AppDomain.CurrentDomain.UnhandledException += async (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception ?? new Exception("Unknown error");
                    await notifier.NotifyExceptionAsync("Unexpected error", ex);
                }
                catch { /* swallow */ }
            };

            TaskScheduler.UnobservedTaskException += async (s, e) =>
            {
                e.SetObserved();
                try { await notifier.NotifyExceptionAsync("Task error", e.Exception); } catch { }
            };

#if ANDROID
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += async (s, e) =>
            {
                try { await notifier.NotifyExceptionAsync("Android error", e.Exception); } catch { }
                e.Handled = true;
            };
#endif
        }

        /// <summary>
        /// Creates a Polly retry policy with exponential backoff for transient HTTP errors.
        /// Handles cold-start scenarios where the API server isn't ready yet.
        /// Retries: 3 times with delays of 2s, 4s, 8s
        /// </summary>
        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError() // Handles HttpRequestException, 5xx, 408
                .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Polly] Retry {retryAttempt} after {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                    });
        }
    }
}
