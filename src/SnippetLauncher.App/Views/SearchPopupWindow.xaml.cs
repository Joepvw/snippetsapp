using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using SnippetLauncher.App.Services;
using SnippetLauncher.App.ViewModels;

namespace SnippetLauncher.App.Views;

public partial class SearchPopupWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private SearchPopupViewModel? _vm;

    public SearchPopupWindow()
    {
        InitializeComponent();
    }

    public void Bind(SearchPopupViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += OnCloseRequested;
    }

    /// <summary>
    /// Shows the popup with proper foreground activation.
    /// Re-entrant: if already visible, just focus the search box.
    /// </summary>
    public void ShowAndActivate()
    {
        if (Visibility == Visibility.Visible)
        {
            FocusSearchBox();
            return;
        }

        Visibility = Visibility.Visible;
        // Toggle Topmost to force this window to the front of the topmost z-order,
        // even when a Topmost dialog (PlaceholderFillDialog) is also open.
        Topmost = false;
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkeyService.BringToForeground(hwnd);
        Activate();
        _vm?.OnActivated();

        // Op de eerste show is de HWND net gemaakt en faalt SetForegroundWindow soms.
        // Retry foregrounding + focus na window-init (ApplicationIdle = na Loaded/Render).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var h = new WindowInteropHelper(this).Handle;
            GlobalHotkeyService.BringToForeground(h);
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
            SearchBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Enable dark title bar on Windows 11
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                HidePopup();
                e.Handled = true;
                break;
            case Key.Up:
                _vm?.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
                _vm?.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                _ = _vm?.ConfirmCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
        }
    }

    private async void OnCloseRequested(object? sender, EventArgs e)
    {
        // Post-Enter sequence:
        // 1. Hide popup (give focus back to target window)
        Visibility = Visibility.Collapsed;
        ReleaseMouseCapture();

        // 2. Wait one frame so the target window gets focus
        await System.Windows.Threading.Dispatcher.Yield();

        // 3. Clipboard is already set by VM before raising CloseRequested
        // (nothing more to do — user presses Ctrl+V in their app)
    }

    private void HidePopup() => Visibility = Visibility.Collapsed;

    private void FocusSearchBox()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        FocusSearchBox();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HidePopup();
    }
}
