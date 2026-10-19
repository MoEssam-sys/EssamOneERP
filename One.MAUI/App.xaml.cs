namespace One.MAUI
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogAndShow(e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => { LogAndShow(e.Exception); e.SetObserved(); };

#if ANDROID
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
            {
                LogAndShow(e.Exception);
                e.Handled = true; // امنع الكراش قدر الإمكان
            };
#endif
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new MainPage()) { Title = "One.ERP" };
        }

        private static void LogAndShow(Exception? ex)
        {
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
                File.AppendAllText(path, $"[{DateTime.Now}] {ex}\n\n");
            }
            catch { /* تجاهل */ }

            MainThread.BeginInvokeOnMainThread(static async () =>
            {
                try
                {
                    // Use the first window's page for single-window applications to avoid obsolete MainPage usage
                    var windows = Application.Current?.Windows;
                    var page = (windows != null && windows.Count > 0) ? windows[0].Page : null;
                    if (page != null)
                    {
                        await page.DisplayAlert("Oops", "Unexpected error occurred.", "OK");
                    }
                }
                catch { /* تجاهل */ }
            });
        }
    }
}