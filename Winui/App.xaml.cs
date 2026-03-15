using System.Text;
using Microsoft.UI.Xaml;

namespace Pelicano;

/// <summary>
/// Pelicano 앱의 WinUI 진입점이다.
/// 메인 창, 트레이, 백그라운드 런타임 호스트를 연결한다.
/// </summary>
public partial class App : Application
{
    private PelicanoHost? _host;
    private MainWindow? _mainWindow;
    private TrayManager? _trayManager;

    public App()
    {
        Environment.SetEnvironmentVariable(
            "MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY",
            AppContext.BaseDirectory);
        InitializeComponent();
        Program.RedirectedActivation += HandleRedirectedActivation;
        UnhandledException += HandleUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _host = new PelicanoHost();
            _mainWindow = new MainWindow(
                _host,
                ExitApplication,
                () => (_trayManager?.IsAvailable ?? false) || _host.IsBackgroundModeAvailable);
            _host.AttachWindow(_mainWindow.WindowHandle);

            try
            {
                _trayManager = new TrayManager(
                    _host.Settings,
                    _mainWindow.ShowWindow,
                    _host.TogglePlainTextOnly,
                    _host.ToggleCaptureAssets,
                    () =>
                    {
                        _mainWindow.DispatcherQueue.TryEnqueue(async () => await _mainWindow.ShowSettingsAsync());
                    },
                    () =>
                    {
                        _mainWindow.DispatcherQueue.TryEnqueue(async () => await _mainWindow.ConfirmAndClearHistoryAsync());
                    },
                    ExitApplication);
            }
            catch (Exception exception)
            {
                _host.AddStartupWarning("시스템 트레이를 초기화하지 못했습니다. 창 닫기 시 백그라운드 동작이 제한될 수 있습니다.");
                ReportFailure("nonfatal-startup", exception);
            }

            _host.ShowWindowRequested += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(_mainWindow.ShowWindow);
            _host.SettingsRequested += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                await _mainWindow.ShowSettingsAsync();
            });
            _host.ClearHistoryRequested += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(async () =>
            {
                await _mainWindow.ConfirmAndClearHistoryAsync();
            });
            _host.ExitRequested += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(ExitApplication);
            _host.SettingsChanged += (_, _) => _mainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                _trayManager?.RefreshState(_host.Settings);
            });

            _mainWindow.Activate();
            _mainWindow.ShowStartupState();
        }
        catch (Exception exception)
        {
            ReportFailure("startup", exception);
            throw;
        }
    }

    /// <summary>
    /// 실제 종료가 필요할 때 런타임 정리와 창 종료를 한 번에 수행한다.
    /// </summary>
    private void ExitApplication()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.PrepareForExit();
        }

        Program.RedirectedActivation -= HandleRedirectedActivation;
        _trayManager?.Dispose();
        _trayManager = null;
        _host?.Dispose();

        if (_mainWindow is not null)
        {
            _mainWindow.Close();
        }

        Exit();
    }

    /// <summary>
    /// 치명적인 UI 예외를 앱 데이터 로그에 남긴다.
    /// </summary>
    private void HandleUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        ReportFailure("runtime", args.Exception);
    }

    private void HandleRedirectedActivation(object? sender, EventArgs args)
    {
        _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow.ShowWindow());
    }

    /// <summary>
    /// 시작 단계/런타임 예외를 로컬 로그 파일에 남겨 원인 추적을 돕는다.
    /// </summary>
    private static void ReportFailure(string phase, Exception exception)
    {
        try
        {
            var dataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Pelicano");
            var logsRoot = Path.Combine(dataRoot, "logs");
            Directory.CreateDirectory(logsRoot);
            var logCategory = phase.Contains("runtime", StringComparison.OrdinalIgnoreCase)
                ? "runtime"
                : "startup";
            var crashPath = Path.Combine(logsRoot, $"{DateTime.Now:yyyyMMdd}.{logCategory}.log");
            var content =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [FATAL] Pelicano {phase} failure{Environment.NewLine}{exception}{Environment.NewLine}";
            File.AppendAllText(crashPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
        }
    }
}
