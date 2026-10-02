using Avalonia;
using System.Diagnostics;

namespace ASMForge.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DiagnosticLog.Info($"Application starting. Version={AppInfo.Version}; .NET={Environment.Version}; OS={Environment.OSVersion}; BaseDir={AppContext.BaseDirectory}; Args={args.Length}");
        // If ASMForge is already open, give it the files (e.g. from double-clicking an .asm) and exit.
        if (!SingleInstance.TryBecomePrimary(args)) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => DiagnosticLog.Error("Unhandled AppDomain exception", e.ExceptionObject as Exception);
        Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { DiagnosticLog.Error("Fatal application exception", ex); throw; }
        finally { Trace.Flush(); DiagnosticLog.Info("Application exiting"); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
