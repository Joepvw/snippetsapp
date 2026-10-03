using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
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
using SnippetLauncher.Core.Updates;

namespace SnippetLauncher.App;

public partial class App : Application
{
    private const string MutexName = "SnippetLauncher_SingleInstance_Mutex";
    private const string PipeName = "SnippetLauncher_IPC";

    private static readonly Version CurrentVersion = ParseCurrentVersion();
    private static readonly string AppVersion = "v" + CurrentVersion.ToString(3);

    private static Version ParseCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (info is not null && Version.TryParse(info, out var parsed))
            return parsed;
        return asm.GetName().Version ?? new Version(0, 0, 0);
    }

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SnippetLauncher");

    private Mutex? _mutex;
    private bool _ownsMutex;
    private ServiceProvider? _services;
    private TaskbarIcon? _trayIcon;
    private MenuItem? _trayRetryItem;
    private MenuItem? _trayUpdateItem;
    private SearchPopupWindow? _popup;
    private EditorWindow? _editor;
    private EditorViewModel? _editorVm;
    private SettingsWindow? _settingsWindow;
    private GitService? _gitService;
    private SnippetRepository? _activeRepository;
    private UpdateNotificationService? _updateNotifier;
    private UpdateCheckResult? _pendingUpdate;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private bool _exiting;
    private EventHandler<SnippetChangedEventArgs>? _snippetSaved;
    private EventHandler<SnippetRemovedEventArgs>? _snippetDeleted;

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
        _ownsMutex = isNew;
        if (!isNew)
        {
            ForwardArgsToRunningInstance(e.Args);
            Shutdown();
            return;
        }

        // ── Settings (must come before DI so first-run can set snippets dir) ─
        SettingsService settingsSvc;
        try { settingsSvc = new SettingsService(AppDataDir); }
        catch (InvalidDataException error)
        {
            MessageBox.Show(error.Message, "Instellingen herstellen", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

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

        var settingsVm = _services.GetRequiredService<SettingsViewModel>();
        settingsVm.RemoteUrlChangedAction = RebuildGitServiceAsync;
        settingsVm.AuthenticateAction = AuthenticateAsync;
        settingsVm.PullIntervalChangedAction = seconds =>
            _gitService?.StartAutoSync(seconds, () => _editorVm?.IsDirty == true);
        settingsVm.SyncAction = SyncNowAsync;
        _settingsWindow = new SettingsWindow(settingsVm);

        // ── Command bus wiring ───────────────────────────────────────────────
        var bus = _services.GetRequiredService<ICommandBus>();
        var clipboard = _services.GetRequiredService<IClipboardService>();

        bus.Subscribe<OpenSearchCommand>(_ =>
        {
            Dispatcher.Invoke(() => { if (!_exiting) _popup.ShowAndActivate(); });
            return Task.CompletedTask;
        });

        bus.Subscribe<QuickAddCommand>(async _ =>
        {
            if (_exiting) return;
            var text = await clipboard.GetTextAsync();
            if (_exiting) return;
            await GetEditor().OpenForQuickAddAsync(text);
        });

        popupVm.CreateSnippetRequested += async (_, title) =>
        {
            if (_exiting) return;
            _popup!.ClosePopup();
            if (await GetEditor().OpenForQuickAddAsync(null)) _editorVm!.EditTitle = title;
        };

        // ── Hotkeys ──────────────────────────────────────────────────────────
        var hotkey = _services.GetRequiredService<IGlobalHotkeyService>();
        hotkey.Register();

        // ── Tray icon ────────────────────────────────────────────────────────
        _trayIcon = BuildTrayIcon();

        // ── Repository load ──────────────────────────────────────────────────
        var snippetRepo = _services.GetRequiredService<SnippetRepository>();
        _activeRepository = snippetRepo;
        _ = InitializeLibraryAsync(settingsSvc, snippetRepo);

        // ── IPC server ───────────────────────────────────────────────────────
        _ = StartIpcServerAsync();

        // ── Update notifier ──────────────────────────────────────────────────
        _updateNotifier = _services.GetRequiredService<UpdateNotificationService>();
        _updateNotifier.UpdateAvailable += result =>
            Dispatcher.BeginInvoke(() => ShowUpdateAvailable(result));
        _updateNotifier.Start();

        Log.Information("Snippet Launcher ready for local input");
    }

    private EditorWindow GetEditor()
    {
        if (_editor is not null) return _editor;
        _editorVm = _services!.GetRequiredService<EditorViewModel>();
        return _editor = new EditorWindow(_editorVm);
    }

    private async Task InitializeLibraryAsync(SettingsService settings, SnippetRepository repository)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            await repository.LoadAllAsync();
            Log.Information("Local library ready in {Milliseconds} ms; {Count} snippets", watch.ElapsedMilliseconds, repository.GetAll().Count);
            await _syncGate.WaitAsync();
            try
            {
                if (_exiting || _gitService is not null) return;
                _gitService = BuildGitService(settings, repository);
                await _gitService.InitOrOpenAsync();
                await repository.LoadAllAsync(); // A clone may have populated files after the first scan.
            }
            finally { _syncGate.Release(); }
        }
        catch (Exception)
        {
            Log.Warning("Library initialization or background sync needs attention; local files preserved");
        }
    }

    private void ShowUpdateAvailable(UpdateCheckResult result)
    {
        if (result.NewVersion is null || result.ReleaseUrl is null) return;

        _pendingUpdate = result;

        if (_trayUpdateItem is not null)
        {
            _trayUpdateItem.Header = $"🆕 Update v{result.NewVersion.ToString(3)} beschikbaar";
            _trayUpdateItem.Visibility = Visibility.Visible;
        }

        try
        {
            _trayIcon?.ShowNotification(
                title: "Update beschikbaar",
                message: $"Snippet Launcher v{result.NewVersion.ToString(3)} is uit.",
                icon: NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not show update toast notification");
        }
    }

    private void OpenPendingUpdate()
    {
        var url = _pendingUpdate?.ReleaseUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not open release URL {Url}", url);
        }
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
            sp.GetRequiredService<WindowsStartupService>()));

        // ── Update checker ────────────────────────────────────────────────────
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            MaxAutomaticRedirections = 3,
        })
        {
            Timeout = TimeSpan.FromSeconds(10),
        });
        services.AddSingleton<IUpdateCheckService>(sp =>
            new GitHubUpdateCheckService(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton(sp => new UpdateNotificationService(
            sp.GetRequiredService<IUpdateCheckService>(),
            sp.GetRequiredService<SettingsService>(),
            CurrentVersion));
    }

    private GitService BuildGitService(SettingsService settingsSvc, SnippetRepository snippetRepo)
    {
        var gitSvc = new GitService(
            settingsSvc.Current.SnippetsDirectory,
            _services!.GetRequiredService<IClock>(),
            _services!.GetRequiredService<IDialogService>(),
            PushQueueStore.ForRepository(AppDataDir, settingsSvc.Current.SnippetsDirectory, settingsSvc.Current.RemoteUrl),
            settingsSvc.Current.RemoteUrl, fileGate: snippetRepo.FileGate);

        // Update tray tooltip on status change
        gitSvc.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() =>
        {
            if (_exiting || !ReferenceEquals(_gitService, gitSvc) || _trayIcon is null) return;
            _trayIcon!.ToolTipText = status switch
            {
                GitSyncStatus.Syncing => $"Snippet Launcher {AppVersion} — Synchroniseren…",
                GitSyncStatus.Behind => $"Snippet Launcher {AppVersion} — Wacht op push",
                GitSyncStatus.Conflict => $"Snippet Launcher {AppVersion} — Conflict opgelost",
                GitSyncStatus.Error => $"Snippet Launcher {AppVersion} — Sync fout (klik rechts voor opties)",
                GitSyncStatus.NoRemote => $"Snippet Launcher {AppVersion} — Geen remote geconfigureerd",
                GitSyncStatus.AuthenticationRequired => $"Snippet Launcher {AppVersion} — Aanmelden nodig (Instellingen)",
                GitSyncStatus.RemoteUnavailable => $"Snippet Launcher {AppVersion} — Controleer account, remote en rechten",
                _ => $"Snippet Launcher {AppVersion}",
            };
            if (_trayRetryItem is not null)
                _trayRetryItem.IsEnabled = status is GitSyncStatus.Error or GitSyncStatus.Behind;
        });

        // Commit + push whenever a snippet is saved or deleted
        _snippetSaved = (_, e) => _ = ObserveGitTaskAsync(gitSvc.CommitAndQueuePushAsync($"snippets: update {e.Snippet.Id}"));
        _snippetDeleted = (_, e) => _ = ObserveGitTaskAsync(gitSvc.CommitAndQueuePushAsync($"snippets: remove {e.Id}"));
        snippetRepo.SnippetChanged += _snippetSaved;
        snippetRepo.SnippetRemoved += _snippetDeleted;

        // Start background sync
        gitSvc.StartAutoSync(settingsSvc.Current.PullIntervalSeconds, () => _editorVm?.IsDirty == true);

        return gitSvc;
    }

    private static async Task ObserveGitTaskAsync(Task task)
    {
        try { await task; }
        catch (Exception) { Log.Warning("A sync operation failed; local changes preserved"); }
    }

    private async Task RebuildGitServiceAsync()
    {
        await _syncGate.WaitAsync();
        try
        {
            if (_activeRepository is null || _exiting) return;
            if (_snippetSaved is not null) _activeRepository.SnippetChanged -= _snippetSaved;
            if (_snippetDeleted is not null) _activeRepository.SnippetRemoved -= _snippetDeleted;
            if (_gitService is not null) await _gitService.DisposeAsync();
            if (_exiting) return;
            var settingsSvc = _services!.GetRequiredService<SettingsService>();
            _gitService = BuildGitService(settingsSvc, _activeRepository);
            await _gitService.InitOrOpenAsync();
            await _activeRepository.LoadAllAsync();
        }
        finally { _syncGate.Release(); }
    }

    private async Task AuthenticateAsync()
    {
        if (!await _syncGate.WaitAsync(0)) throw new InvalidOperationException("Er loopt al een synchronisatieactie.");
        try
        {
            if (_gitService is null || _exiting) throw new InvalidOperationException("Synchronisatie is nog niet beschikbaar.");
            await _gitService.AuthenticateAsync();
            if (_activeRepository is not null) await _activeRepository.LoadAllAsync();
        }
        finally { _syncGate.Release(); }
    }

    private async Task SyncNowAsync()
    {
        if (!await _syncGate.WaitAsync(0)) throw new InvalidOperationException("Er loopt al een synchronisatieactie.");
        try
        {
            var svc = _gitService;
            if (svc is null || _exiting) throw new InvalidOperationException("Synchronisatie is niet beschikbaar.");
            await svc.PullNowAsync();
            await svc.RetryPushNowAsync();
            if (_activeRepository is not null) await _activeRepository.LoadAllAsync();
        }
        finally { _syncGate.Release(); }
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

        _trayUpdateItem = new MenuItem
        {
            Header = "🆕 Update beschikbaar",
            Visibility = Visibility.Collapsed,
            FontWeight = FontWeights.SemiBold,
        };
        _trayUpdateItem.Click += (_, _) => OpenPendingUpdate();
        menu.Items.Add(_trayUpdateItem);

        var searchItem = new MenuItem { Header = "Zoeken (Ctrl+Shift+Space)" };
        searchItem.Click += (_, _) => _popup?.ShowAndActivate();
        menu.Items.Add(searchItem);

        var editorItem = new MenuItem { Header = "Snippets beheren…" };
        editorItem.Click += (_, _) => { if (_exiting) return; var editor = GetEditor(); editor.Show(); editor.Activate(); };
        menu.Items.Add(editorItem);

        menu.Items.Add(new Separator());

        var syncItem = new MenuItem { Header = "Nu synchroniseren" };
        syncItem.Click += (_, _) => _ = ObserveGitTaskAsync(SyncNowAsync());
        menu.Items.Add(syncItem);

        _trayRetryItem = new MenuItem { Header = "Push opnieuw proberen", IsEnabled = false };
        _trayRetryItem.Click += (_, _) => { if (_gitService is not null) _ = ObserveGitTaskAsync(_gitService.RetryPushNowAsync()); };
        menu.Items.Add(_trayRetryItem);

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "Instellingen…" };
        settingsItem.Click += (_, _) => { _settingsWindow?.Show(); _settingsWindow?.Activate(); };
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "Afsluiten" };
        exitItem.Click += (_, _) => _ = RequestExitAsync();
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

    private async Task RequestExitAsync()
    {
        if (_exiting) return;
        if (_editorVm is not null && !await _editorVm.PrepareForExitAsync()) return;
        _exiting = true;
        if (_editor is not null) _editor.IsEnabled = false;
        if (_settingsWindow is not null) _settingsWindow.IsEnabled = false;
        try
        {
            _services?.GetService<IGlobalHotkeyService>()?.Dispose();
            _popup?.ClosePopup();
            var git = _gitService;
            git?.StopAutoSyncAndCancelNetwork();
            await ShutdownDrain.RunAsync(_activeRepository?.DrainAsync() ?? Task.CompletedTask,
                git is null ? null : () => git.CommitAndQueuePushAsync("snippets: preserve final local changes"),
                TimeSpan.FromSeconds(15));
            if (git is not null)
            {
                await git.DisposeAsync();
                if (ReferenceEquals(_gitService, git)) _gitService = null;
            }
            if (_activeRepository is not null) await _activeRepository.DisposeAsync();
            _editorVm?.Dispose();
            if (_services is not null) await _services.DisposeAsync();
            _updateNotifier = null;
            _services = null;
            Shutdown();
        }
        catch (Exception)
        {
            Log.Error("Shutdown could not finish cleanly; local data preserved for recovery");
            MessageBox.Show("Afsluiten kon niet volledig worden afgerond. De app sluit nu af; je opgeslagen bestanden blijven bewaard en lokale wijzigingen worden bij de volgende start hersteld.",
                "Afsluiten", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            _ = RequestExitAsync();
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Snippet Launcher exiting");
        // Normal and emergency exits already used bounded async shutdown. Never
        // repeat an unbounded repository drain while the process is exiting.
        if (!_exiting)
        {
            if (_updateNotifier is not null)
            {
                try { _updateNotifier.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch (Exception) { Log.Warning("Update notifier dispose failed"); }
            }
            _services?.GetService<IGlobalHotkeyService>()?.Dispose();
            _activeRepository?.Dispose();
            try { _gitService?.Dispose(); }
            catch (Exception) { Log.Warning("Git worker could not stop before process exit; local files retained"); }
        }
        _trayIcon?.Dispose();
        // Normal exit drained and asynchronously disposed the provider before Shutdown.
        // A timed-out worker may still be alive. Keep ownership until the OS
        // terminates this process, preventing a replacement app from starting early.
        var keepMutexUntilProcessExit = _exiting && _gitService is not null;
        if (!keepMutexUntilProcessExit && _ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { /* mutex held on a different thread — let Dispose clean it up */ }
        }
        if (!keepMutexUntilProcessExit) _mutex?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

file sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
