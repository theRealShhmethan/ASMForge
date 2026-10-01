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
