using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ASMForge.App.Views;

internal enum SaveChoice { Save, DontSave, Cancel }

/// <summary>"Save changes?" prompt shown before unsaved work would be discarded.</summary>
internal sealed class SavePromptDialog : Window
{
    private SavePromptDialog(string question, string details)
    {
        Title = "Unsaved changes";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "Save", IsDefault = true };
        var dontSave = new Button { Content = "Don't Save" };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        save.Click += (_, _) => Close(SaveChoice.Save);
        dontSave.Click += (_, _) => Close(SaveChoice.DontSave);
        cancel.Click += (_, _) => Close(SaveChoice.Cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = question, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { save, dontSave, cancel }
                }
            }
        };
    }

    /// <summary>Shows the prompt; closing it with the window's X counts as Cancel.</summary>
    public static async Task<SaveChoice> ShowAsync(Window owner, string question, string details)
    {
        var result = await new SavePromptDialog(question, details).ShowDialog<SaveChoice?>(owner);
        return result ?? SaveChoice.Cancel;
    }
}

/// <summary>
/// Prompts for a new 32-bit register or memory value. Accepts hex (0x2A), binary (0b101010),
/// signed or unsigned decimal (-5, 4294967291) and character literals ('A').
/// </summary>
internal sealed class EditValueDialog : Window
{
    private readonly TextBox _input;
    private readonly TextBlock _error;

    private EditValueDialog(string title, uint current)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = new TextBox { Text = $"0x{current:X8}", FontFamily = new FontFamily("Cascadia Mono,Consolas") };
        _error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var ok = new Button { Content = "Set", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"Current value: 0x{current:X8} ({unchecked((int)current)})", Opacity = 0.85 },
                _input,
                new TextBlock { Text = "Hex 0x2A, binary 0b101010, decimal -5 or 42, or a character 'A'.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                _error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel }
                }
            }
        };
        Opened += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    private void Accept()
    {
        if (TryParseValue(_input.Text ?? string.Empty, out var value)) { Close(value); return; }
        _error.Text = "Not a valid 32-bit value.";
        _error.IsVisible = true;
    }

    /// <summary>Returns the new value, or null if cancelled.</summary>
    public static Task<uint?> ShowAsync(Window owner, string title, uint current) =>
        new EditValueDialog(title, current).ShowDialog<uint?>(owner);

    internal static bool TryParseValue(string text, out uint value)
    {
        value = 0;
        var s = text.Trim().Replace("_", string.Empty);
        if (s.Length == 0) return false;
        try
        {
            if (s.Length == 3 && s[0] == '\'' && s[2] == '\'') { value = s[1]; return true; }
            var negative = s.StartsWith('-');
            if (negative) s = s[1..];
            long parsed = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(s[2..], 16)
                : s.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(s[2..], 2)
                : long.Parse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
            if (negative) parsed = -parsed;
            if (parsed < int.MinValue || parsed > uint.MaxValue) return false;
            value = unchecked((uint)parsed);
            return true;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>Help > About: version, description, runtime, credits and where settings/logs live.</summary>
internal sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = "About ASMForge";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var paths = new TextBox
        {
            Text = $"Settings: {AppSettings.SettingsPath}\nLogs:     {DiagnosticLog.LogDirectory}",
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 12
        };

        var openLogs = new Button { Content = "Open Log Folder" };
        openLogs.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(DiagnosticLog.LogDirectory);
                Process.Start(new ProcessStartInfo(DiagnosticLog.LogDirectory) { UseShellExecute = true });
            }
            catch (Exception ex) { DiagnosticLog.Error("Could not open the log folder", ex); }
        };
        var close = new Button { Content = "Close", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "ASMForge", FontSize = 26, FontWeight = FontWeight.Bold },
                new TextBlock { Text = $"Version {AppInfo.Version}", Opacity = 0.8 },
                new TextBlock
                {
                    Text = "A MIPS assembly IDE and simulator built with C# and Avalonia: a MARS-compatible assembler " +
                           "with machine-code output, a debugger with breakpoints and step back, memory and register " +
                           "viewers, and a C# API for controlling the simulated machine.",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = $".NET {Environment.Version} · {RuntimeInformation.OSDescription}",
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Pseudo-instruction definitions from MARS 4.5 by Pete Sanderson and Kenneth Vollmar " +
                           "(MIT license). Built with Avalonia, AvaloniaEdit and Roslyn.",
                    Opacity = 0.8,
                    TextWrapping = TextWrapping.Wrap
                },
                paths,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { openLogs, close }
                }
            }
        };
    }
}
