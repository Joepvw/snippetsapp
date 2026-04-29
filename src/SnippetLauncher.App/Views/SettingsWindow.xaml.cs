using System.Windows;
using SnippetLauncher.App.ViewModels;

namespace SnippetLauncher.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Hide instead of close so the window can be re-opened
        e.Cancel = true;
        Hide();
    }
}
