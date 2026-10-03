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
    private int _showGeneration;

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
        var generation = ++_showGeneration;
        // Toggle Topmost to force this window to the front of the topmost z-order,
        // even when a Topmost dialog (PlaceholderFillDialog) is also open.
        Topmost = false;
        Topmost = true;
        var hwnd = new WindowInteropHelper(this).Handle;
        GlobalHotkeyService.BringToForeground(hwnd);
        Activate();
        _vm?.OnActivated();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        // Op de eerste show is de HWND net gemaakt en faalt SetForegroundWindow soms.
        // Retry foregrounding + focus na window-init (ApplicationIdle = na Loaded/Render).
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (generation != _showGeneration || !IsVisible || SearchBox.Text.Length > 0) return;
            var h = new WindowInteropHelper(this).Handle;
            GlobalHotkeyService.BringToForeground(h);
            Activate();
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
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
        HidePopup();
        ReleaseMouseCapture();

        // 2. Wait one frame so the target window gets focus
        await System.Windows.Threading.Dispatcher.Yield();

        // 3. Clipboard is already set by VM before raising CloseRequested
        // (nothing more to do — user presses Ctrl+V in their app)
    }

    public void ClosePopup() => HidePopup();

    private void HidePopup()
    {
        ++_showGeneration;
        _vm?.OnHidden();
        Visibility = Visibility.Collapsed;
    }

    private void FocusSearchBox()
    {
        SearchBox.Focus();
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
