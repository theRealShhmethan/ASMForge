using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ASMForge.App.Views;

public partial class NewItemDialog : Window
{
    public NewItemResult? Result { get; private set; }

    // Parameterless constructor required by Avalonia's XAML runtime loader and designer.
    public NewItemDialog() : this(null) { }

    public NewItemDialog(string? defaultLocation)
    {
        InitializeComponent();
        LocationBox.Text = !string.IsNullOrWhiteSpace(defaultLocation)
            ? defaultLocation
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void KindBox_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (NameBox is null) return;
        NameBox.Text = KindBox.SelectedIndex switch { 1 => "Program.cs", 2 => "MyProject", 3 => "program.pseudo", _ => "main.asm" };
        HintText.Text = KindBox.SelectedIndex switch
        {
            1 => "Creates a C# source file. If a project is open, the project folder is used by default and the file appears in Explorer immediately.",
            2 => "Creates an ASMForge project folder with starter main.asm and Program.cs files.",
            3 => "Creates a pseudocode file. Press F3 to generate MIPS assembly from it (name.asm next to it), or F5 to generate and run.",
            _ => "Creates a MIPS assembly source file. If a project is open, the project folder is used by default."
        };
    }

    private async void Browse_Click(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose location", AllowMultiple = false });
        if (folders.Count > 0) LocationBox.Text = folders[0].Path.LocalPath;
    }

    private void Cancel_Click(object? s, RoutedEventArgs e) => Close(false);

    private void Create_Click(object? s, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? "").Trim();
        var location = (LocationBox.Text ?? "").Trim();
        if (name.Length == 0 || location.Length == 0)
        {
            HintText.Text = "Name and location are required.";
            return;
        }
        Result = new NewItemResult(KindBox.SelectedIndex, name, location);
        Close(true);
    }
}

public sealed record NewItemResult(int Kind, string Name, string Location);
