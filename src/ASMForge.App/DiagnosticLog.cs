using System.Diagnostics;

namespace ASMForge.App;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    public static readonly string LogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ASMForge", "Logs");
    public static readonly string LogPath = Path.Combine(LogDirectory, $"ASMForge-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [ASMForge] [{level}] {message}" + (ex is null ? "" : Environment.NewLine + ex);
        Debug.WriteLine(line);
        Trace.WriteLine(line);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
