using System.Reflection;

namespace ASMForge.App;

/// <summary>Application name and version, read from the assembly (set in Directory.Build.props).</summary>
internal static class AppInfo
{
    public static string Version { get; } = ReadVersion();

    public static string DisplayName => $"ASMForge v{Version}";

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        // The SDK may append "+<commit hash>" to the informational version; show only the version.
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational)) return informational.Split('+')[0];
        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
