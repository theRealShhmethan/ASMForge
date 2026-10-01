using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace ASMForge.App.Views;

public partial class NewItemDialog : Window
{
    public NewItemResult? Result { get; private set; }
    public NewItemDialog() { InitializeComponent(); LocationBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
    private void KindBox_SelectionChanged(object? s, SelectionChangedEventArgs e)
    {
        if (NameBox is null) return;
        NameBox.Text = KindBox.SelectedIndex switch { 1 => "Program.cs", 2 => "MyProject", _ => "main.asm" };
        HintText.Text = KindBox.SelectedIndex switch { 1 => "Creates a C# source file. C# execution support is planned for a future ASMForge version.", 2 => "Creates an ASMForge project folder with a project file and starter main.asm.", _ => "Creates a MIPS assembly source file." };
    }
    private async void Browse_Click(object? s, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose location", AllowMultiple = false });
        if (folders.Count > 0) LocationBox.Text = folders[0].Path.LocalPath;
    }
    private void Cancel_Click(object? s, RoutedEventArgs e) => Close(false);
    private void Create_Click(object? s, RoutedEventArgs e)
    {
        var name = (NameBox.Text ?? "").Trim(); var location = (LocationBox.Text ?? "").Trim();
        if (name.Length == 0 || location.Length == 0) { HintText.Text = "Name and location are required."; return; }
        Result = new NewItemResult(KindBox.SelectedIndex, name, location); Close(true);
    }
}
public sealed record NewItemResult(int Kind, string Name, string Location);
