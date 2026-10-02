using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ASMForge.App.Views;

namespace ASMForge.App;

public partial class App : Application
{
    // More unexpected errors than this within the window means something is stuck in a loop; let the app exit.
    private const int MaxErrorsPerWindow = 8;
    private static readonly TimeSpan ErrorWindow = TimeSpan.FromSeconds(30);
    private readonly Queue<DateTime> _recentErrors = new();
    private bool _showingError;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Unexpected exceptions on the UI thread: log, keep running, and tell the user instead of closing.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            DiagnosticLog.Error("Unhandled UI exception", e.Exception);
            if (!TooManyRecentErrors())
            {
                e.Handled = true;
                ShowErrorDialog(e.Exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // Files passed on the command line (double-clicking an .asm / .pseudo file) and from later launches.
            window.OpenCommandLineFiles(desktop.Args ?? Array.Empty<string>());
            SingleInstance.FilesReceived += files => Dispatcher.UIThread.Post(() => window.OpenCommandLineFiles(files, bringToFront: true));
        }
        base.OnFrameworkInitializationCompleted();
    }

    private bool TooManyRecentErrors()
    {
        var now = DateTime.UtcNow;
        _recentErrors.Enqueue(now);
        while (_recentErrors.Count > 0 && now - _recentErrors.Peek() > ErrorWindow) _recentErrors.Dequeue();
        return _recentErrors.Count > MaxErrorsPerWindow;
    }

    // One dialog at a time; errors that happen while it is open are only logged.
    private void ShowErrorDialog(Exception exception)
    {
        if (_showingError || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } owner }) return;
        _showingError = true;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await new ErrorDialog(exception).ShowDialog(owner); }
            catch (Exception dialogError) { DiagnosticLog.Error("Could not show the error dialog", dialogError); }
            finally { _showingError = false; }
        });
    }
}
