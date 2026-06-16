using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// Shell 계층 조립 루트 — 메시지 윈도우 호스트 + 시스템 연동 구현 묶음.
/// Program.cs가 입력 계층(Win32InputLayer)과 함께 <c>OverlayCoordinator</c>에 주입한다.
///
/// 메시지 윈도우 호스트(STA 펌프)를 트레이/모니터변경/포그라운드가 공유한다(SHELL-LAYER.md §1).
/// </summary>
public sealed class Win32ShellLayer : IDisposable
{
    private readonly MessageWindowHost _host = new();
    private readonly Win32MonitorProvider _monitors;
    private readonly Win32TrayIcon _tray;
    private readonly Win32ForegroundAppMonitor _foreground;
    private readonly JsonFileSettingsStore _settings;
    private bool _disposed;

    public IClock Clock { get; } = new Win32Clock();
    public ISettingsStore Settings => _settings;
    public IBrandingProvider Branding { get; }
    public ILaunchAtLogin LaunchAtLogin { get; } = new RegistryLaunchAtLogin();
    public IMonitorProvider Monitors => _monitors;
    public ITrayIcon Tray => _tray;
    public IForegroundAppMonitor Foreground => _foreground;

    public Win32ShellLayer(string trayTooltip = "Cluxo")
    {
        _settings = new JsonFileSettingsStore();
        Branding = new FileBrandingProvider();
        _monitors = new Win32MonitorProvider(_host);
        _tray = new Win32TrayIcon(_host, trayTooltip);
        _foreground = new Win32ForegroundAppMonitor(_host);
    }

    /// <summary>트레이 메뉴를 열 때마다 현재 상태로 항목을 빌드(체크 표시 갱신).</summary>
    public void SetTrayMenuProvider(Func<IReadOnlyList<TrayMenuItem>> provider)
        => _tray.MenuProvider = provider;

    /// <summary>트레이 풍선 알림(예: 마우스 후킹 재설치 T2).</summary>
    public void ShowTrayBalloon(string title, string text) => _tray.ShowBalloon(title, text);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.Dispose();
        _foreground.Dispose();
        _monitors.Dispose();
        _settings.Dispose(); // 대기 중 설정 flush
        _host.Dispose();
    }
}
