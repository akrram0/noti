using Microsoft.UI.Xaml;
using Noti.App.ViewModels;
using Noti.Core;
using Noti.Storage;

namespace Noti.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var store = new MarkdownNoteStore();
            var service = new NoteService(store);
            var viewModel = new MainViewModel(service);
            await viewModel.InitializeAsync();

            var mainWindow = new MainWindow(viewModel);
            _window = mainWindow;
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            LogCrash("App.OnLaunched", ex);
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash("App.UnhandledException", e.Exception, e.Message);
    }

    private void OnCurrentDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void LogCrash(string source, Exception? ex, string? extraMessage = null)
    {
        try
        {
            var logDir = AppSettings.AppDataDirectory;
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff 'UTC'");
            var details = $"[{timestamp}] Source: {source}\n";
            if (!string.IsNullOrWhiteSpace(extraMessage))
                details += $"Message: {extraMessage}\n";
            if (ex != null)
                details += $"Exception: {ex.GetType().FullName}: {ex.Message}\nStackTrace:\n{ex.StackTrace}\n";
            if (ex?.InnerException != null)
                details += $"InnerException: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\nStackTrace:\n{ex.InnerException.StackTrace}\n";
            details += new string('-', 80) + "\n";

            File.AppendAllText(logPath, details);
        }
        catch
        {
            // Never mask crashes if the logging disk path fails
        }
    }
}
