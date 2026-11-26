using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Serilog;
using SnippetLauncher.App.Services;
using SnippetLauncher.App.ViewModels;

namespace SnippetLauncher.App.Views;

public partial class SearchPopupWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_KEYMENU = 0xF100;

    private SearchPopupViewModel? _vm;
    private int _showGeneration;
    private bool _shownBefore;

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
    /// Renders a throwaway copy of the popup once, off-screen and without taking focus, so the
    /// process-wide first-render costs are paid before the user presses the hotkey.
    /// </summary>
    public static async Task PrewarmAsync(SearchPopupViewModel throwawayVm)
    {
        var window = new SearchPopupWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowActivated = false,
            DataContext = throwawayVm,
        };
        try
        {
            window.Show();
            throwawayVm.OnActivated();
            await throwawayVm.WhenQueryCompletedAsync();
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            throwawayVm.OnHidden();
            window.Close();
        }
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

        var watch = Stopwatch.StartNew();
        var firstShow = !_shownBefore;
        _shownBefore = true;
        // Setting Visibility only queues the show. The first show also sizes and centres the
        // window, so do it synchronously here: the position must not depend on whether the
        // (prewarmed, so near-instant) first query has already filled the result list.
        if (firstShow) Show();
        else Visibility = Visibility.Visible;
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
            Log.Debug("Search popup usable in {Milliseconds} ms (first show: {FirstShow})", watch.ElapsedMilliseconds, firstShow);
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

        // The foreground workaround taps ALT (GlobalHotkeyService.BringToForeground). When that
        // tap lands in this window, Windows enters menu mode and swallows the next keystrokes,
        // so the first typed characters never reach the search box. This popup has no menu.
        HwndSource.FromHwnd(hwnd)?.AddHook(SuppressKeyMenu);
    }

    private static IntPtr SuppressKeyMenu(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_KEYMENU) handled = true;
        return IntPtr.Zero;
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
