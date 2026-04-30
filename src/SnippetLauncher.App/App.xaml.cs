using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SnippetLauncher.App.Services;
using SnippetLauncher.App.ViewModels;
using SnippetLauncher.App.Views;
using SnippetLauncher.Core.Abstractions;
using SnippetLauncher.Core.Commands;
using SnippetLauncher.Core.Infrastructure;
using SnippetLauncher.Core.Placeholders;
using SnippetLauncher.Core.Search;
using SnippetLauncher.Core.Settings;
using SnippetLauncher.Core.Storage;
using SnippetLauncher.Core.Sync;

namespace SnippetLauncher.App;

public partial class App : Application
{
    private const string MutexName = "SnippetLauncher_SingleInstance_Mutex";
    private const string PipeName = "SnippetLauncher_IPC";

    private static readonly string AppVersion =
        "v" + (Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "?");

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnippetLauncher");

    private Mutex? _mutex;
    private ServiceProvider? _services;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _trayRetryItem;
    private SearchPopupWindow? _popup;
    private EditorWindow? _editor;
    private SettingsWindow? _settingsWindow;
    private GitService? _gitService;
    private SnippetRepository? _activeRepository;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataDir);

        // ── Logging ──────────────────────────────────────────────────────────
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(AppDataDir, "log", "app.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("Snippet Launcher starting");

        // ── Crash handlers ───────────────────────────────────────────────────
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Fatal(ex.Exception, "Unhandled UI exception");
            ShowCrashDialog(ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            if (ex.ExceptionObject is Exception exc)
                Log.Fatal(exc, "Unhandled domain exception");
        };

        // ── Single-instance guard ────────────────────────────────────────────
        _mutex = new Mutex(true, MutexName, out var isNew);
        if (!isNew)
        {
            ForwardArgsToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        // ── Settings (must come before DI so first-run can set snippets dir) ─
        var settingsSvc = new SettingsService(AppDataDir);

        // ── First-run wizard ─────────────────────────────────────────────────
        if (settingsSvc.IsFirstRun)
        {
            var wizardVm = new FirstRunWizardViewModel(settingsSvc);
            var wizard = new FirstRunWizardWindow(wizardVm);
            var result = wizard.ShowDialog();
            if (result != true)
            {
                // User closed the wizard without completing → quit
                Shutdown();
                return;
            }
        }

        // ── Dependency injection ─────────────────────────────────────────────
        var services = new ServiceCollection();
        ConfigureServices(services, settingsSvc);
        _services = services.BuildServiceProvider();

        // ── Autostart self-heal (re-bind registry path if exe moved) ─────────
        if (settingsSvc.Current.StartAtLoginEnabled)
        {
            var startupSvc = _services.GetRequiredService<WindowsStartupService>();
            if (!startupSvc.IsEnabled())
            {
                try { startupSvc.Enable(); }
                catch (Exception ex) { Log.Warning(ex, "Autostart self-heal failed"); }
            }
        }

        // ── Windows ──────────────────────────────────────────────────────────
        _popup = new SearchPopupWindow();
        var popupVm = _services.GetRequiredService<SearchPopupViewModel>();
        _popup.Bind(popupVm);

        _editor = new EditorWindow(_services.GetRequiredService<EditorViewModel>());

        var settingsVm = _services.GetRequiredService<SettingsViewModel>();
        settingsVm.RepoPathChanged += OnRepoPathChanged;
        settingsVm.RemoteUrlChanged += OnRemoteUrlChanged;
        settingsVm.SyncNowRequested += (_, _) => _ = SyncNowAsync();
        _settingsWindow = new SettingsWindow(settingsVm);

        // ── Command bus wiring ───────────────────────────────────────────────
        var bus = _services.GetRequiredService<ICommandBus>();
        var clipboard = _services.GetRequiredService<IClipboardService>();

        bus.Subscribe<OpenSearchCommand>(_ =>
        {
            Dispatcher.Invoke(() => _popup.ShowAndActivate());
            return Task.CompletedTask;
        });

        bus.Subscribe<QuickAddCommand>(async _ =>
        {
            var text = await clipboard.GetTextAsync();
            Dispatcher.Invoke(() => _editor!.OpenForQuickAdd(text));
        });

        popupVm.CreateSnippetRequested += (_, title) =>
        {
            _popup!.Visibility = Visibility.Collapsed;
            _editor!.OpenForQuickAdd(null);
            _services!.GetRequiredService<EditorViewModel>().EditTitle = title;
        };

        // ── Hotkeys ──────────────────────────────────────────────────────────
        var hotkey = _services.GetRequiredService<IGlobalHotkeyService>();
        hotkey.Register();

        // ── Tray icon ────────────────────────────────────────────────────────
        _trayIcon = BuildTrayIcon();

        // ── Repository load ──────────────────────────────────────────────────
        var snippetRepo = _services.GetRequiredService<SnippetRepository>();
        _activeRepository = snippetRepo;
        _ = snippetRepo.LoadAllAsync();

        // ── Git sync ─────────────────────────────────────────────────────────
        _gitService = BuildGitService(settingsSvc, snippetRepo);

        // ── IPC server ───────────────────────────────────────────────────────
        _ = StartIpcServerAsync();

        Log.Information("Snippet Launcher started — snippets dir: {Dir}", settingsSvc.Current.SnippetsDirectory);
    }

    private void ConfigureServices(IServiceCollection services, SettingsService settingsSvc)
    {
        var snippetsDir = settingsSvc.Current.SnippetsDirectory;
        if (string.IsNullOrEmpty(snippetsDir))
        {
            // Fallback — should not happen after wizard
            snippetsDir = Path.Combine(AppDataDir, "snippets");
            settingsSvc.Current.SnippetsDirectory = snippetsDir;
            settingsSvc.Save();
        }

        Directory.CreateDirectory(snippetsDir);

        services.AddSingleton(settingsSvc);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<PlaceholderFillContext>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<ICommandBus, InProcCommandBus>();
        services.AddSingleton(sp => new UsageStore(
            Path.Combine(AppDataDir, "usage.json"),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new SnippetRepository(
            snippetsDir,
            sp.GetRequiredService<UsageStore>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new SearchService(
            sp.GetRequiredService<SnippetRepository>(),
            sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new PlaceholderEngine(sp.GetRequiredService<IClock>()));
        services.AddSingleton<SearchPopupViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<IGlobalHotkeyService>(sp =>
            new GlobalHotkeyService(
                sp.GetRequiredService<ICommandBus>(),
                sp.GetRequiredService<SettingsService>()));
        services.AddSingleton<WindowsStartupService>();
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<SettingsService>(),
            sp.GetRequiredService<IGlobalHotkeyService>(),
            sp.GetRequiredService<SnippetRepository>(),
            sp.GetRequiredService<WindowsStartupService>()));
    }

    private GitService BuildGitService(SettingsService settingsSvc, SnippetRepository snippetRepo)
    {
        var pushQueuePath = Path.Combine(AppDataDir, "push-queue.json");
        var gitSvc = new GitService(
            settingsSvc.Current.SnippetsDirectory,
            _services!.GetRequiredService<IClock>(),
            _services!.GetRequiredService<IDialogService>(),
            new PushQueueStore(pushQueuePath),
            settingsSvc.Current.RemoteUrl);

        // Update tray tooltip on status change
        gitSvc.StatusChanged += (_, status) => Dispatcher.Invoke(() =>
        {
            _trayIcon!.ToolTipText = status switch
            {
                GitSyncStatus.Syncing => $"Snippet Launcher {AppVersion} — Synchroniseren…",
                GitSyncStatus.Behind => $"Snippet Launcher {AppVersion} — Wacht op push",
                GitSyncStatus.Conflict => $"Snippet Launcher {AppVersion} — Conflict opgelost",
                GitSyncStatus.Error => $"Snippet Launcher {AppVersion} — Sync fout (klik rechts voor opties)",
                GitSyncStatus.NoRemote => $"Snippet Launcher {AppVersion} — Geen remote geconfigureerd",
                _ => $"Snippet Launcher {AppVersion}",
            };
            if (_trayRetryItem is not null)
                _trayRetryItem.IsEnabled = status is GitSyncStatus.Error or GitSyncStatus.Behind;
        });

        // Commit + push whenever a snippet is saved or deleted
        snippetRepo.SnippetChanged += (_, e) =>
            _ = gitSvc.CommitAndQueuePushAsync($"snippets: update {e.Snippet.Id}");
        snippetRepo.SnippetRemoved += (_, e) =>
            _ = gitSvc.CommitAndQueuePushAsync($"snippets: remove {e.Id}");

        // Start background sync
        var editorVm = _services!.GetRequiredService<EditorViewModel>();
        gitSvc.StartAutoSync(settingsSvc.Current.PullIntervalSeconds, () => editorVm.IsDirty);
        _ = gitSvc.InitOrOpenAsync();

        return gitSvc;
    }

    private void OnRepoPathChanged(object? sender, string newPath)
    {
        Dispatcher.Invoke(async () =>
        {
            Directory.CreateDirectory(newPath);

            // Drain old git service
            _gitService?.Dispose();

            var newRepo = new SnippetRepository(
                newPath,
                _services!.GetRequiredService<UsageStore>(),
                _services!.GetRequiredService<IClock>());
            await newRepo.LoadAllAsync();

            var settingsSvc = _services!.GetRequiredService<SettingsService>();
            _activeRepository = newRepo;
            _gitService = BuildGitService(settingsSvc, newRepo);

            Log.Information("Repository reloaded at {Path}", newPath);
        });
    }

    private void OnRemoteUrlChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_activeRepository is null) return;
            _gitService?.Dispose();
            var settingsSvc = _services!.GetRequiredService<SettingsService>();
            _gitService = BuildGitService(settingsSvc, _activeRepository);
            Log.Information("GitService rebuilt for new remote URL");
        });
    }

    private async Task SyncNowAsync()
    {
        var svc = _gitService;
        if (svc is null) return;
        await svc.PullNowAsync();
        await svc.RetryPushNowAsync();
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = $"Snippet Launcher {AppVersion}",
            IconSource = GetDefaultIcon(),
        };
        icon.ForceCreate();

        var menu = new ContextMenu();

        var searchItem = new MenuItem { Header = "Zoeken (Ctrl+Shift+Space)" };
        searchItem.Click += (_, _) => _popup?.ShowAndActivate();
        menu.Items.Add(searchItem);

        var editorItem = new MenuItem { Header = "Snippets beheren…" };
        editorItem.Click += (_, _) => { _editor?.Show(); _editor?.Activate(); };
        menu.Items.Add(editorItem);

        menu.Items.Add(new Separator());

        var syncItem = new MenuItem { Header = "Nu synchroniseren" };
        syncItem.Click += (_, _) => _ = SyncNowAsync();
        menu.Items.Add(syncItem);

        _trayRetryItem = new MenuItem { Header = "Push opnieuw proberen", IsEnabled = false };
        _trayRetryItem.Click += (_, _) => _ = _gitService?.RetryPushNowAsync();
        menu.Items.Add(_trayRetryItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Instellingen…" };
        settingsItem.Click += (_, _) => { _settingsWindow?.Show(); _settingsWindow?.Activate(); };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Afsluiten" };
        exitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(exitItem);

        icon.ContextMenu = menu;
        icon.TrayLeftMouseDown += (_, _) => _popup?.ShowAndActivate();

        return icon;
    }

    private static System.Windows.Media.ImageSource? GetDefaultIcon()
    {
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
            new Uri("pack://application:,,,/Resources/tray.ico"),
            System.Windows.Media.Imaging.BitmapCreateOptions.None,
            System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static void ForwardArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            using var writer = new StreamWriter(client);
            writer.WriteLine(args.Length > 0 ? string.Join("|", args) : "open");
        }
        catch { }
    }

    private async Task StartIpcServerAsync()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync();
                Dispatcher.Invoke(() => HandleIpcMessage(message));
            }
            catch { }
        }
    }

    private void HandleIpcMessage(string? message)
    {
        if (string.IsNullOrEmpty(message) || message == "open")
            _popup?.ShowAndActivate();
    }

    private static void ShowCrashDialog(Exception ex)
    {
        var logPath = Path.Combine(AppDataDir, "log");
        MessageBox.Show(
            $"Er is een onverwachte fout opgetreden:\n\n{ex.Message}\n\n" +
            $"Logbestanden staan in:\n{logPath}\n\n" +
            "Stuur deze bij een bugreport.",
            "Snippet Launcher — Onverwachte fout",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Snippet Launcher exiting");
        _services?.GetService<IGlobalHotkeyService>()?.Dispose();
        _services?.GetService<SnippetRepository>()?.Dispose();
        _gitService?.Dispose();
        _trayIcon?.Dispose();
        _services?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

file sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
