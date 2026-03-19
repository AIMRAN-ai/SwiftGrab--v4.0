using SwiftGrab.Models;
using SwiftGrab.Services;
using SwiftGrab.Services.RustInterop;
using SwiftGrab.ViewModels;
using SwiftGrab.Views;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;
using System.Drawing;
using WpfApp = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace SwiftGrab
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : WpfApp
    {
        private BrowserIntegrationService? _browserService;
        private NativeHostBridgeService? _bridgeService;
        private TorrentDownloadService? _torrentService;
        private UpdateService? _updateService;
        private WinForms.NotifyIcon? _trayIcon;
        private bool _silentMode;

        /// <summary>
        /// The application-wide DI service provider.
        /// </summary>
        public static IServiceProvider Services { get; private set; } = null!;

        internal static ILogger<App> Logger { get; private set; } = Microsoft.Extensions.Logging.Abstractions.NullLogger<App>.Instance;

        /// <summary>
        /// Indicates if the application is running as a packaged MSIX app.
        /// </summary>
        public static bool IsPackagedApp { get; private set; }

        /// <summary>
        /// Gets the application data directory (handles MSIX vs unpackaged scenarios).
        /// </summary>
        public static string AppDataDirectory { get; private set; } = null!;

        public App()
        {
            // Global exception handlers for MSIX compatibility - MUST be first
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            // Detect MSIX package identity early
            IsPackagedApp = DetectMsixPackage();
            AppDataDirectory = GetAppDataDirectory();

            // Log startup information immediately
            LogStartupInfo();
        }

        /// <summary>
        /// Detects if running as a packaged MSIX application.
        /// </summary>
        private static bool DetectMsixPackage()
        {
            try
            {
                // GetCurrentPackageFullName returns ERROR_INSUFFICIENT_BUFFER (122) if packaged
                // or APPMODEL_ERROR_NO_PACKAGE (15700) if not packaged
                int length = 0;
                int result = GetCurrentPackageFullName(ref length, null);
                return result != APPMODEL_ERROR_NO_PACKAGE;
            }
            catch
            {
                return false;
            }
        }

        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

        /// <summary>
        /// Gets the appropriate data directory based on package context.
        /// </summary>
        private static string GetAppDataDirectory()
        {
            string baseDir;

            if (IsPackagedApp)
            {
                // For MSIX packaged apps, use LocalApplicationData which is virtualized
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwiftGrab");
            }
            else
            {
                // For unpackaged apps, use LocalApplicationData
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SwiftGrab");
            }

            try
            {
                Directory.CreateDirectory(baseDir);
            }
            catch
            {
                // Fallback to temp directory if we can't create the app data directory
                baseDir = Path.Combine(Path.GetTempPath(), "SwiftGrab");
                Directory.CreateDirectory(baseDir);
            }

            return baseDir;
        }

        /// <summary>
        /// Logs startup information for debugging MSIX issues.
        /// </summary>
        private static void LogStartupInfo()
        {
            try
            {
                var logPath = Path.Combine(AppDataDirectory, "startup.log");
                var info = $"""
                    [SwiftGrab Startup - {DateTime.Now:yyyy-MM-dd HH:mm:ss}]
                    IsPackagedApp: {IsPackagedApp}
                    AppDataDirectory: {AppDataDirectory}
                    CurrentDirectory: {Environment.CurrentDirectory}
                    ExecutablePath: {Environment.ProcessPath}
                    CommandLine: {Environment.CommandLine}
                    OSVersion: {Environment.OSVersion}
                    Is64BitProcess: {Environment.Is64BitProcess}
                    CLR Version: {Environment.Version}
                    ---

                    """;
                File.AppendAllText(logPath, info);
            }
            catch
            {
                // Ignore logging failures during startup
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("Dispatcher", e.Exception);
            WpfMessageBox.Show($"An error occurred:\n\n{e.Exception.Message}", "SwiftGrab Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("AppDomain", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("Task", e.Exception);
            e.SetObserved();
        }

        private static void LogException(string source, Exception ex)
        {
            try
            {
                var logPath = Path.Combine(AppDataDirectory, "crash.log");

                var message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n";
                File.AppendAllText(logPath, message);
            }
            catch
            {
                // Cannot log, ignore
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                // Build DI container
                var serviceCollection = new ServiceCollection();
                ConfigureServices(serviceCollection);
                Services = serviceCollection.BuildServiceProvider();

                Logger = Services.GetRequiredService<ILogger<App>>();
                Logger.LogInformation("SwiftGrab startup initiated. IsPackagedApp={IsPackaged}, DataDir={DataDir}",
                    IsPackagedApp, AppDataDirectory);

                LogStartupPhase("DI container built successfully");

                LogStartupPhase("Validating prerequisites");
                ValidatePrerequisites();
                LogStartupPhase("Prerequisites validated");

                // Initialize Rust download engine (optional — gracefully falls back to C# engine)
                try
                {
                    var settings = SettingsService.Instance.Settings;
                    var rustInitialized = RustDownloadBridge.Instance.Initialize(settings);
                    LogStartupPhase(rustInitialized
                        ? $"Rust download engine initialized (v{RustDownloadBridge.Instance.GetVersion()})"
                        : "Rust engine unavailable — using C# download engine");
                }
                catch (Exception rustEx)
                {
                    LogException("RustEngine", rustEx);
                    LogStartupPhase("Rust engine failed to load — using C# download engine");
                }

                // Initialize torrent service (deferred, wrapped in try-catch)
                try
                {
                    _torrentService = Services.GetRequiredService<TorrentDownloadService>();
                    _ = SafeFireAndForgetAsync(_torrentService.RestoreAsync(), "TorrentRestore");

                    _torrentService.TorrentAdded += (s, item) => Dispatcher.Invoke(() => ShowTrayNotification("Torrent added", item.Name));
                    _torrentService.TorrentCompleted += (s, item) => Dispatcher.Invoke(() => ShowTrayNotification("Torrent completed", item.Name));
                    _torrentService.StatusMessage += (s, msg) => Dispatcher.Invoke(() => ShowTrayNotification("Torrent", msg));
                }
                catch (Exception torrentEx)
                {
                    LogException("TorrentService", torrentEx);
                }

                // Initialize system tray (wrapped for MSIX compatibility)
                try
                {
                    InitializeTrayIcon();
                }
                catch (Exception trayEx)
                {
                    LogException("TrayIcon", trayEx);
                }

                // Handle command-line arguments
                if (e.Args.Length > 0)
                {
                    HandleCommandLineArgs(e.Args);
                }

                // Initialize browser integration service (deferred)
                try
                {
                    _browserService = Services.GetRequiredService<BrowserIntegrationService>();
                    _browserService.DownloadRequested += OnBrowserDownloadRequested;
                }
                catch (Exception browserEx)
                {
                    LogException("BrowserService", browserEx);
                }

                // Start the HTTP bridge for browser extension communication (always-on)
                try
                {
                    _bridgeService = Services.GetRequiredService<NativeHostBridgeService>();
                    _bridgeService.DownloadRequested += OnBrowserDownloadRequested;
                    _bridgeService.VideoDownloadRequested += OnBrowserDownloadRequested;
                    _bridgeService.OpenAppRequested += (_, _) => Dispatcher.Invoke(EnsureMainWindowVisible);
                    _bridgeService.OpenHistoryRequested += (_, _) => Dispatcher.Invoke(EnsureMainWindowVisible);

                    var downloadManager = Services.GetRequiredService<DownloadManager>();
                    _bridgeService.GetActiveDownloadCount = () => downloadManager.ActiveDownloadCount;
                    _bridgeService.GetCompletedDownloadCount = () =>
                    {
                        try
                        {
                            var history = DownloadHistoryService.Instance.Load();
                            return history.Count(d => d.Status == DownloadStatus.Completed);
                        }
                        catch { return 0; }
                    };

                    _bridgeService.Start();
                    LogStartupPhase("NativeHostBridge started on port 19615");
                }
                catch (Exception bridgeEx)
                {
                    LogException("NativeHostBridge", bridgeEx);
                }

                // Start background update check
                try
                {
                    _updateService = Services.GetRequiredService<UpdateService>();
                    _ = SafeFireAndForgetAsync(CheckForUpdatesAsync(), "UpdateCheck");
                }
                catch (Exception updateEx)
                {
                    LogException("UpdateService", updateEx);
                }

                // Create and show MainWindow from DI
                var mainWindow = Services.GetRequiredService<MainWindow>();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (InvalidOperationException prereqEx)
            {
                LogException("Prerequisites", prereqEx);
                LogStartupPhase($"Prerequisites FAILED: {prereqEx.Message}");

                if (!_silentMode)
                {
                    WpfMessageBox.Show(
                        $"SwiftGrab can't start because a prerequisite check failed:\n\n{prereqEx.Message}\n\nSee startup.log for details.",
                        "SwiftGrab - Prerequisite Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                Shutdown(1);
            }
            catch (Exception ex)
            {
                LogException("Startup", ex);
                LogStartupPhase($"FATAL STARTUP ERROR: {ex}");
                WpfMessageBox.Show($"Failed to start SwiftGrab:\n\n{ex.Message}", "SwiftGrab Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static void ValidatePrerequisites()
        {
            if (string.IsNullOrWhiteSpace(AppDataDirectory))
                throw new InvalidOperationException("App data directory is not set.");

            try
            {
                Directory.CreateDirectory(AppDataDirectory);
                var probeFile = Path.Combine(AppDataDirectory, "write.probe");
                File.WriteAllText(probeFile, "ok");
                File.Delete(probeFile);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException($"No write access to app data directory: {AppDataDirectory}", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Cannot access app data directory: {AppDataDirectory}", ex);
            }
        }

        /// <summary>
        /// Logs a startup phase to the startup.log file for debugging MSIX launch issues.
        /// </summary>
        private static void LogStartupPhase(string phase)
        {
            try
            {
                var logPath = Path.Combine(AppDataDirectory, "startup.log");
                var message = $"[{DateTime.Now:HH:mm:ss.fff}] {phase}\n";
                File.AppendAllText(logPath, message);
            }
            catch
            {
                // Ignore logging failures
            }
        }

        /// <summary>
        /// Registers all services, view-models, and windows in the DI container.
        /// </summary>
        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddDebug();
                builder.AddProvider(new FileLoggerProvider(GetLogDirectory()));
            });

            services.AddSingleton<AppDiagnostics>();

            // Singletons — shared across the whole application lifetime
            services.AddSingleton<SettingsService>(_ => SettingsService.Instance);
            services.AddSingleton<ThemeService>(_ => ThemeService.Instance);

            // Core download infrastructure — each download creates its own DownloadCoordinator
            services.AddTransient<SegmentDownloader>();
            services.AddTransient<DownloadService>();
            services.AddSingleton<YtDlpService>();
            services.AddSingleton<DownloadManager>();

            // Auxiliary services
            services.AddSingleton<VideoDownloadService>();
            services.AddSingleton<TorrentDownloadService>();
            services.AddSingleton<BrowserIntegrationService>();
            services.AddSingleton<NativeHostBridgeService>();
            services.AddSingleton<UpdateService>();

            // ViewModels
            services.AddTransient<MainViewModel>();

            // Windows
            services.AddTransient<MainWindow>();
        }

        private static string GetLogDirectory()
        {
            var baseDir = Path.Combine(AppDataDirectory, "logs");

            Directory.CreateDirectory(baseDir);
            return baseDir;
        }

        private async Task CheckForUpdatesAsync()
        {
            try
            {
                if (_updateService is null) return;

                var manifest = await _updateService.CheckForUpdateAsync().ConfigureAwait(false);
                if (manifest is null) return;

                // Prompt on UI thread
                await Dispatcher.InvokeAsync(async () =>
                {
                    var message = string.IsNullOrWhiteSpace(manifest.Notes)
                        ? $"A new version ({manifest.Version}) is available. Do you want to download and install it now?"
                        : $"A new version ({manifest.Version}) is available.\n\nChanges:\n{manifest.Notes}\n\nDownload and install now?";

                    var result = WpfMessageBox.Show(message, "SwiftGrab - Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var ok = await _updateService!.DownloadAndRunInstallerAsync(manifest).ConfigureAwait(false);
                            if (!ok)
                            {
                                WpfMessageBox.Show("Failed to download or start the installer.", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            WpfMessageBox.Show($"Failed to update: {ex.Message}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }).Task.ConfigureAwait(false);
            }
            catch
            {
                // Do not crash the app for update check failures
            }
        }

        private void HandleCommandLineArgs(string[] args)
        {
            var arg = args[0];

            // Handle protocol registration request (elevated)
            if (arg.Equals("--register-protocol", StringComparison.OrdinalIgnoreCase))
            {
                var success = ProtocolHandler.RegisterProtocol();
                // Also register magnet: protocol
                RegisterMagnetProtocol();
                Environment.Exit(success ? 0 : 1);
                return;
            }

            // Handle magnet protocol registration
            if (arg.Equals("--register-magnet", StringComparison.OrdinalIgnoreCase))
            {
                var success = RegisterMagnetProtocol();
                Environment.Exit(success ? 0 : 1);
                return;
            }

            // Handle native host mode
            if (arg.Equals("--native-host", StringComparison.OrdinalIgnoreCase))
            {
                RunAsNativeHost();
                return;
            }

            // Handle export extension files
            if (arg.Equals("--export-extension", StringComparison.OrdinalIgnoreCase))
            {
                var directory = args.Length > 1 ? args[1] : BrowserIntegrationService.GetExtensionDirectory();
                BrowserIntegrationService.ExportExtensionFiles(directory);
                if (!_silentMode)
                {
                    WpfMessageBox.Show($"Extension files exported to:\n{directory}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                Environment.Exit(0);
                return;
            }

            // Handle swiftgrab:// protocol URL
            if (arg.StartsWith("swiftgrab:", StringComparison.OrdinalIgnoreCase))
            {
                var request = BrowserIntegrationService.ParseProtocolUrl(arg);
                if (request != null)
                {
                    QueueDownloadRequest(request);
                }
                return;
            }

            // Handle magnet: links
            if (arg.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                HandleMagnetLink(arg);
                return;
            }

            // Handle .torrent files
            if (arg.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && File.Exists(arg))
            {
                HandleTorrentFile(arg);
                return;
            }

            // Handle direct URL passed as argument
            if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var request = new BrowserDownloadRequest
                {
                    Url = arg,
                    AutoStart = true
                };
                QueueDownloadRequest(request);
            }
        }

        private async void RunAsNativeHost()
        {
            // Hide the main window when running as native host
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                if (_browserService == null)
                {
                    _browserService = Services.GetRequiredService<BrowserIntegrationService>();
                }

                await _browserService.StartNativeHostListenerAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogException("NativeHost", ex);
                Console.Error.WriteLine($"Native host error: {ex.Message}");
            }

            Shutdown(0);
        }

        private void ShowTrayNotification(string title, string message)
        {
            if (_silentMode) return;
            if (_trayIcon == null) return;

            try
            {
                _trayIcon.BalloonTipTitle = title;
                _trayIcon.BalloonTipText = message;
                _trayIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _trayIcon.ShowBalloonTip(3000);
            }
            catch
            {
            }
        }

        private void OnBrowserDownloadRequested(object? sender, BrowserDownloadRequest request)
        {
            // Ensure we're on the UI thread
            Dispatcher.Invoke(() => QueueDownloadRequest(request));
        }

        private void QueueDownloadRequest(BrowserDownloadRequest request)
        {
            try
            {
                // Get or create the main window
                var mainWindow = MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    // If app was started from protocol/extension, show window
                    mainWindow = Services.GetRequiredService<MainWindow>();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                }

                // Ensure window is visible
                EnsureMainWindowVisible();

                // Detect if this is a video URL — route to video resolution dialog for quality selection
                if (IsVideoUrl(request.Url))
                {
                    var videoDialog = new Views.VideoResolutionDialog(request.Url) { Owner = mainWindow };
                    videoDialog.ShowDialog();

                    if (videoDialog.DialogResultOk &&
                        videoDialog.SelectedVideoFormat != null &&
                        videoDialog.VideoInfo != null &&
                        mainWindow.DataContext is MainViewModel videoVm)
                    {
                        videoVm.AddVideoDownload(
                            videoDialog.VideoInfo,
                            videoDialog.SelectedVideoFormat,
                            videoDialog.SavePath);

                        ShowTrayNotification("Video download started",
                            $"{videoDialog.VideoInfo.Title} ({videoDialog.SelectedVideoFormat.QualityLabel})");
                    }
                    return;
                }

                // Regular file download — show SaveFileDialog so user can choose where to save
                var fileName = ExtractFileName(request);
                var defaultFolder = SettingsService.Instance.Settings.DefaultDownloadFolder;
                Directory.CreateDirectory(defaultFolder);

                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "SwiftGrab — Save Download As",
                    FileName = fileName,
                    InitialDirectory = defaultFolder,
                    Filter = BuildFilterFromFileName(fileName),
                    OverwritePrompt = true
                };

                if (saveDialog.ShowDialog(mainWindow) != true)
                {
                    // User canceled — do not download
                    Logger.LogInformation("Browser download canceled by user: {Url}", request.Url);
                    return;
                }

                var fullPath = saveDialog.FileName;

                // Ensure save directory exists
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Add download to queue via ViewModel and start it
                if (mainWindow.DataContext is MainViewModel viewModel)
                {
                    viewModel.AddDownloadFromUrl(
                        url: request.Url,
                        savePath: fullPath,
                        category: "Browser",
                        description: $"From {request.Browser ?? "extension"}",
                        startImmediately: true);

                    // Show the active download window for real-time progress
                    if (viewModel.SelectedItem != null)
                    {
                        var activeWindow = new Views.ActiveDownloadWindow(viewModel.SelectedItem) { Owner = mainWindow };
                        activeWindow.Show();
                    }

                    ShowTrayNotification("Download started", Path.GetFileName(fullPath));
                    Logger.LogInformation("Browser download queued: {FileName} from {Url}", Path.GetFileName(fullPath), request.Url);
                }
                else
                {
                    Logger.LogWarning("MainViewModel not available for browser download");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to queue browser download: {Url}", request.Url);
                ShowTrayNotification("Download failed", $"Could not start: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds a file dialog filter string based on the file name extension.
        /// </summary>
        private static string BuildFilterFromFileName(string fileName)
        {
            var ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                return "All files (*.*)|*.*";

            var description = ext.TrimStart('.').ToUpperInvariant();
            return $"{description} files (*{ext})|*{ext}|All files (*.*)|*.*";
        }

        /// <summary>
        /// Checks if a URL points to a known video platform.
        /// </summary>
        private static bool IsVideoUrl(string url)
        {
            var lower = url.ToLowerInvariant();
            return lower.Contains("youtube.com/watch") || lower.Contains("youtu.be/") ||
                   lower.Contains("vimeo.com/") || lower.Contains("dailymotion.com/video") ||
                   lower.Contains("twitch.tv/videos") || lower.Contains("twitter.com/") ||
                   lower.Contains("x.com/") || lower.Contains("tiktok.com/@") ||
                   lower.Contains("instagram.com/") || lower.Contains("facebook.com/") ||
                   lower.Contains("rumble.com/") || lower.Contains("bitchute.com/");
        }

        private static string ExtractFileName(BrowserDownloadRequest request)
        {
            if (!string.IsNullOrEmpty(request.Filename))
            {
                return Path.GetFileName(request.Filename);
            }

            try
            {
                var uri = new Uri(request.Url);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    return fileName;
                }
            }
            catch
            {
                // Ignore URI parsing errors
            }

            return $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        private async void HandleMagnetLink(string magnetLink)
        {
            try
            {
                EnsureMainWindowVisible();

                if (_torrentService != null)
                {
                    var item = await _torrentService.AddAsync(magnetLink).ConfigureAwait(false);
                    Dispatcher.Invoke(() =>
                    {
                        if (!_silentMode)
                        {
                            WpfMessageBox.Show($"Added torrent: {item.Name}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_silentMode)
                        {
                            WpfMessageBox.Show("Torrent service not initialized.", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_silentMode)
                    {
                        WpfMessageBox.Show($"Failed to add torrent: {ex.Message}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
        }

        private async void HandleTorrentFile(string filePath)
        {
            try
            {
                EnsureMainWindowVisible();

                if (_torrentService != null)
                {
                    var item = await _torrentService.AddAsync(filePath).ConfigureAwait(false);
                    Dispatcher.Invoke(() =>
                    {
                        if (!_silentMode)
                        {
                            WpfMessageBox.Show($"Added torrent: {item.Name}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_silentMode)
                        {
                            WpfMessageBox.Show("Torrent service not initialized.", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_silentMode)
                    {
                        WpfMessageBox.Show($"Failed to add torrent: {ex.Message}", "SwiftGrab", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _trayIcon = new WinForms.NotifyIcon
                {
                    Text = "SwiftGrab",
                    Icon = SystemIcons.Application,
                    Visible = true
                };

                var menu = new WinForms.ContextMenuStrip();
                var showItem = new WinForms.ToolStripMenuItem("Show SwiftGrab", null, (s, e) => EnsureMainWindowVisible());
                var silentItem = new WinForms.ToolStripMenuItem("Silent Mode") { CheckOnClick = true, Checked = _silentMode };
                silentItem.CheckedChanged += (s, e) => _silentMode = silentItem.Checked;
                var exitItem = new WinForms.ToolStripMenuItem("Exit", null, (s, e) => ExitFromTray());

                menu.Items.Add(showItem);
                menu.Items.Add(silentItem);
                menu.Items.Add(new WinForms.ToolStripSeparator());
                menu.Items.Add(exitItem);

                _trayIcon.ContextMenuStrip = menu;
                _trayIcon.DoubleClick += (s, e) => EnsureMainWindowVisible();
            }
            catch
            {
            }
        }

        private void ExitFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                if (MainWindow is MainWindow mw)
                {
                    mw.AllowClose();
                    mw.Close();
                }
            });

            Shutdown(0);
        }

        private void EnsureMainWindowVisible()
        {
            Dispatcher.Invoke(() =>
            {
                var mainWindow = MainWindow as MainWindow;
                if (mainWindow == null)
                {
                    mainWindow = Services.GetRequiredService<MainWindow>();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                }

                mainWindow.RestoreFromTray();
            });
        }

        private static bool RegisterMagnetProtocol()
        {
            try
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return false;

                using var key = Microsoft.Win32.Registry.ClassesRoot.CreateSubKey("magnet");
                if (key == null) return false;

                key.SetValue("", "URL:Magnet Protocol");
                key.SetValue("URL Protocol", "");

                using var iconKey = key.CreateSubKey("DefaultIcon");
                iconKey?.SetValue("", $"\"{exePath}\",0");

                using var commandKey = key.CreateSubKey(@"shell\open\command");
                commandKey?.SetValue("", $"\"{exePath}\" \"%1\"");

                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            // Shutdown AI Intelligence Engine and all AI subsystems
            try
            {
                SwiftGrabAIEngine.Instance.Dispose();
                AIBridgeOrchestrator.Instance.Dispose();
                AdaptiveDownloadEngine.Instance.Dispose();
                DownloadHealthMonitor.Instance.Dispose();
            }
            catch
            {
                // Best-effort AI cleanup during exit
            }

            // Shutdown Rust download engine
            try
            {
                RustDownloadBridge.Instance.Dispose();
            }
            catch
            {
                // Ignore native shutdown errors during exit
            }

            _browserService?.StopNativeHostListener();

            if (_bridgeService is not null)
            {
                await _bridgeService.StopAsync().ConfigureAwait(false);
                _bridgeService.Dispose();
                _bridgeService = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            if (_torrentService != null)
            {
                await _torrentService.DisposeAsync().ConfigureAwait(false);
            }

            if (Services is IDisposable disposable)
            {
                disposable.Dispose();
            }

            base.OnExit(e);
        }

                /// <summary>
                /// Awaits a task and logs any exception instead of letting it crash the process.
                /// </summary>
                private static async Task SafeFireAndForgetAsync(Task task, string source)
                {
                    try
                    {
                        await task.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogException(source, ex);
                    }
                }

                // ── Lightweight file-based ILoggerProvider ──────────────────────────

                private sealed class FileLoggerProvider : ILoggerProvider
                {
                    private readonly string _logDirectory;

                    public FileLoggerProvider(string logDirectory)
                    {
                        _logDirectory = logDirectory;
                    }

                    public ILogger CreateLogger(string categoryName) => new FileLogger(_logDirectory, categoryName);

                    public void Dispose()
                    {
                    }
                }

                private sealed class FileLogger : ILogger
                {
                    private static readonly object Sync = new();
                    private readonly string _categoryName;
                    private readonly string _logFilePath;

                    public FileLogger(string logDirectory, string categoryName)
                                {
                                    _categoryName = categoryName;
                                    _logFilePath = Path.Combine(logDirectory, $"app_{DateTime.UtcNow:yyyyMMdd}.log");
                                }

                                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

                                public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

                                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                                    Func<TState, Exception?, string> formatter)
                                {
                                    if (!IsEnabled(logLevel))
                                    {
                                        return;
                                    }

                                    var message = formatter(state, exception);
                                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{logLevel}] [{_categoryName}] {message}";
                                    if (exception is not null)
                                    {
                                        line += $"{Environment.NewLine}{exception}";
                                    }

                                    try
                                    {
                                        lock (Sync)
                                        {
                                            File.AppendAllText(_logFilePath, line + Environment.NewLine);
                                        }
                                    }
                                    catch
                                    {
                                        System.Diagnostics.Debug.WriteLine(line);
                                    }
                                }
                            }
                        }
                    }
