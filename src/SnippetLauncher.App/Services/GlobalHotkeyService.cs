using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Commands;
using SnippetLauncher.Core.Settings;

namespace SnippetLauncher.App.Services;

/// <summary>
/// Registers global hotkeys via Win32 RegisterHotKey using a hidden HwndSource.
/// Must be created and used on the UI thread.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HK_SEARCH = 1;
    private const int HK_QUICKADD = 2;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly ICommandBus _commandBus;
    private readonly SettingsService _settings;
    private HwndSource? _hwndSource;
    private bool _registered;

    private HotkeyBinding _searchBinding;
    private HotkeyBinding _quickAddBinding;

    public GlobalHotkeyService(ICommandBus commandBus, SettingsService settings)
    {
        _commandBus = commandBus;
        _settings = settings;
        _searchBinding = HotkeyBinding.TryParse(settings.Current.SearchHotkey) ?? DefaultSearch;
        _quickAddBinding = HotkeyBinding.TryParse(settings.Current.QuickAddHotkey) ?? DefaultQuickAdd;
    }

    private static HotkeyBinding DefaultSearch => HotkeyBinding.TryParse("Ctrl+Shift+Space")!.Value;
    private static HotkeyBinding DefaultQuickAdd => HotkeyBinding.TryParse("Ctrl+Shift+N")!.Value;

    public void Register()
    {
        if (_registered) return;

        var p = new HwndSourceParameters("SnippetLauncherHotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW
        };
        _hwndSource = new HwndSource(p);
        _hwndSource.AddHook(WndProc);

        RegisterOne(HK_SEARCH, _searchBinding, "Search");
        RegisterOne(HK_QUICKADD, _quickAddBinding, "QuickAdd");

        _registered = true;
    }

    public void Unregister()
    {
        if (!_registered || _hwndSource is null) return;
        var hWnd = _hwndSource.Handle;
        UnregisterHotKey(hWnd, HK_SEARCH);
        UnregisterHotKey(hWnd, HK_QUICKADD);
        _registered = false;
    }

    public bool TryRebindSearch(HotkeyBinding newBinding)
    {
        if (!_registered || _hwndSource is null) return false;
        var hWnd = _hwndSource.Handle;
        UnregisterHotKey(hWnd, HK_SEARCH);
        if (RegisterHotKey(hWnd, HK_SEARCH, newBinding.Modifiers | MOD_NOREPEAT, newBinding.VirtualKey))
        {
            _searchBinding = newBinding;
            _settings.Current.SearchHotkey = newBinding.ToString();
            return true;
        }
        // Rollback
        RegisterHotKey(hWnd, HK_SEARCH, _searchBinding.Modifiers | MOD_NOREPEAT, _searchBinding.VirtualKey);
        return false;
    }

    public bool TryRebindQuickAdd(HotkeyBinding newBinding)
    {
        if (!_registered || _hwndSource is null) return false;
        var hWnd = _hwndSource.Handle;
        UnregisterHotKey(hWnd, HK_QUICKADD);
        if (RegisterHotKey(hWnd, HK_QUICKADD, newBinding.Modifiers | MOD_NOREPEAT, newBinding.VirtualKey))
        {
            _quickAddBinding = newBinding;
            _settings.Current.QuickAddHotkey = newBinding.ToString();
            return true;
        }
        RegisterHotKey(hWnd, HK_QUICKADD, _quickAddBinding.Modifiers | MOD_NOREPEAT, _quickAddBinding.VirtualKey);
        return false;
    }

    private void RegisterOne(int id, HotkeyBinding binding, string name)
    {
        var hWnd = _hwndSource!.Handle;
        if (!RegisterHotKey(hWnd, id, binding.Modifiers | MOD_NOREPEAT, binding.VirtualKey))
        {
            MessageBox.Show(
                $"Kon globale hotkey {binding} niet registreren. " +
                "Mogelijk gebruikt een andere applicatie dezelfde combinatie. " +
                "Pas de hotkey aan in Instellingen.",
                "Snippet Launcher — Hotkey conflict",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case HK_SEARCH:
                _commandBus.Publish(new OpenSearchCommand());
                handled = true;
                break;
            case HK_QUICKADD:
                _commandBus.Publish(new QuickAddCommand());
                handled = true;
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Brings the given window to the foreground using the ALT-key trick.
    /// Falls back to AttachThreadInput on Win11 24H2+ if needed.
    /// </summary>
    public static void BringToForeground(IntPtr targetHwnd)
    {
        keybd_event(VK_MENU, 0, 0, IntPtr.Zero);
        SetForegroundWindow(targetHwnd);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        BringWindowToTop(targetHwnd);

        var foreground = GetForegroundWindow();
        if (foreground != targetHwnd)
        {
            var fgThread = GetWindowThreadProcessId(foreground, out _);
            var appThread = GetCurrentThreadId();
            if (fgThread != appThread)
            {
                AttachThreadInput(fgThread, appThread, true);
                SetForegroundWindow(targetHwnd);
                BringWindowToTop(targetHwnd);
                AttachThreadInput(fgThread, appThread, false);
            }
        }
    }

    public void Dispose()
    {
        Unregister();
        _hwndSource?.Dispose();
    }
}
