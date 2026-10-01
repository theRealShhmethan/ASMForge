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

/// <summary>Edit > Go to Line: asks for a line number within 1..maxLine.</summary>
internal sealed class GoToLineDialog : Window
{
    private readonly TextBox _input;
    private readonly int _maxLine;

    private GoToLineDialog(int currentLine, int maxLine)
    {
        _maxLine = maxLine;
        Title = "Go to Line";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _input = new TextBox { Text = currentLine.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var go = new Button { Content = "Go", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        go.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"Line number (1 - {maxLine}):" },
                _input,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { go, cancel } }
            }
        };
        Opened += (_, _) => { _input.Focus(); _input.SelectAll(); };
    }

    private void Accept()
    {
        if (int.TryParse(_input.Text, out var line)) Close(Math.Clamp(line, 1, _maxLine));
        else _input.SelectAll();
    }

    public static Task<int?> ShowAsync(Window owner, int currentLine, int maxLine) =>
        new GoToLineDialog(currentLine, maxLine).ShowDialog<int?>(owner);
}

/// <summary>Edit > Go to Label: a filterable list of (label, line); Enter or double-click picks one.</summary>
internal sealed class GoToLabelDialog : Window
{
    private readonly IReadOnlyList<(string Name, int Line)> _labels;
    private readonly TextBox _filter;
    private readonly ListBox _list;

    private GoToLabelDialog(IReadOnlyList<(string Name, int Line)> labels)
    {
        _labels = labels;
        Title = "Go to Label";
        Width = 380;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _filter = new TextBox { Watermark = "Type to filter labels" };
        _list = new ListBox { FontFamily = new FontFamily("Cascadia Mono,Consolas") };
        _filter.TextChanged += (_, _) => ApplyFilter();
        _filter.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Accept(); }
            else if (e.Key == Avalonia.Input.Key.Down && _list.ItemCount > 0) { e.Handled = true; _list.SelectedIndex = Math.Min(_list.SelectedIndex + 1, _list.ItemCount - 1); }
            else if (e.Key == Avalonia.Input.Key.Up && _list.ItemCount > 0) { e.Handled = true; _list.SelectedIndex = Math.Max(_list.SelectedIndex - 1, 0); }
            else if (e.Key == Avalonia.Input.Key.Escape) { e.Handled = true; Close(null); }
        };
        _list.DoubleTapped += (_, _) => Accept();

        var grid = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(_filter);
        Grid.SetRow(_list, 1);
        _list.Margin = new Thickness(0, 8, 0, 0);
        grid.Children.Add(_list);
        Content = grid;

        ApplyFilter();
        Opened += (_, _) => _filter.Focus();
    }

    private void ApplyFilter()
    {
        var filter = (_filter.Text ?? string.Empty).Trim();
        _list.ItemsSource = _labels
            .Where(l => filter.Length == 0 || l.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(l => new LabelItem(l.Name, l.Line))
            .ToList();
        _list.SelectedIndex = _list.ItemCount > 0 ? 0 : -1;
    }

    private void Accept()
    {
        if (_list.SelectedItem is LabelItem item) Close(item.Line);
    }

    public static Task<int?> ShowAsync(Window owner, IReadOnlyList<(string Name, int Line)> labels) =>
        new GoToLabelDialog(labels).ShowDialog<int?>(owner);

    private sealed record LabelItem(string Name, int Line)
    {
        public override string ToString() => $"{Name,-24} line {Line}";
    }
}

/// <summary>Shown for unexpected errors: the app keeps running, with details to copy and the log folder.</summary>
internal sealed class ErrorDialog : Window
{
    public ErrorDialog(Exception exception)
    {
        Title = "ASMForge - Unexpected error";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var details = $"ASMForge {AppInfo.Version} · .NET {Environment.Version} · {RuntimeInformation.OSDescription}\n\n{exception}";
        var copy = new Button { Content = "Copy Details" };
        copy.Click += async (_, _) =>
        {
            try { if (Clipboard is not null) await Clipboard.SetTextAsync(details); }
            catch (Exception ex) { DiagnosticLog.Error("Copy to clipboard failed", ex); }
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
        var close = new Button { Content = "Continue", IsDefault = true, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Something went wrong, but ASMForge is still running.", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Save your work. If this keeps happening, copy the details or send the log file.", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 },
                new TextBlock { Text = exception.Message, TextWrapping = TextWrapping.Wrap },
                new Expander
                {
                    Header = "Details",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Content = new TextBox
                    {
                        Text = details,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.NoWrap,
                        MaxHeight = 260,
                        FontFamily = new FontFamily("Cascadia Mono,Consolas"),
                        FontSize = 11
                    }
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { copy, openLogs, close }
                }
            }
        };
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
