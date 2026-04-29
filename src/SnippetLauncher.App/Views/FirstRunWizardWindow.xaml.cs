using System.Windows;
using SnippetLauncher.App.ViewModels;

namespace SnippetLauncher.App.Views;

public partial class FirstRunWizardWindow : Window
{
    public FirstRunWizardWindow(FirstRunWizardViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.Completed += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
    }
}
