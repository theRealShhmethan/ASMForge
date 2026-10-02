using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ASMForge.App;

/// <summary>
/// Keeps one ASMForge window per installation. A second launch (for example double-clicking an .asm file while
/// ASMForge is open) hands its file paths to the running copy over a named pipe and exits. Copies in different
/// folders (an installed copy and a Visual Studio debug build) are independent.
/// </summary>
internal static class SingleInstance
{
    private static readonly string Id = BuildId();
    private static Mutex? _mutex; // held for the lifetime of the primary instance

    /// <summary>Raised on a background thread when another launch sends its files (possibly none).</summary>
    public static event Action<string[]>? FilesReceived;

    /// <summary>True if this process should start the app; false if the files were handed to a running copy.</summary>
    public static bool TryBecomePrimary(string[] args)
    {
        _mutex = new Mutex(initiallyOwned: true, $"Local\\ASMForge-{Id}", out var createdNew);
        if (createdNew)
        {
            StartServer();
            return true;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            foreach (var arg in args) writer.WriteLine(Path.GetFullPath(arg));
            DiagnosticLog.Info($"Handed {args.Length} file(s) to the running ASMForge.");
            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn($"Could not reach the running ASMForge ({ex.Message}); starting another window.");
            return true;
        }
    }

    private static string PipeName => $"ASMForge-{Id}";

    private static void StartServer()
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var files = new List<string>();
                    while (reader.ReadLine() is { } line)
                        if (line.Length > 0) files.Add(line);
                    FilesReceived?.Invoke(files.ToArray());
                }
                catch (Exception ex)
                {
                    DiagnosticLog.Warn($"Single-instance pipe error: {ex.Message}");
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "ASMForge single instance" };
        thread.Start();
    }

    // Per user and per installation folder.
    private static string BuildId()
    {
        var key = Environment.UserName + "|" + AppContext.BaseDirectory.ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 8);
    }
}
