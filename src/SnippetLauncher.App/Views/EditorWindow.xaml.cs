using System.Windows;
using SnippetLauncher.App.ViewModels;

namespace SnippetLauncher.App.Views;

public partial class EditorWindow : Window
{
    private readonly EditorViewModel _vm;

    public EditorWindow(EditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    /// <summary>Opens the editor with a new snippet pre-filled with <paramref name="body"/>.</summary>
    public void OpenForQuickAdd(string? body)
    {
        Show();
        Activate();
        if (body is not null)
            _vm.NewSnippetCommand.Execute(body);
        else
            _vm.NewSnippetEmptyClipboardCommand.Execute(null);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Hide instead of close so the window can be re-shown
        e.Cancel = true;
        Hide();
    }
}
