using System.Text;
using Cluxo.Core.Platform;
using SN = Cluxo.Windows.App.Shell.ShellNativeMethods;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="IForegroundAppMonitor"/> — SetWinEventHook(EVENT_SYSTEM_FOREGROUND)(SHELL-LAYER.md §6).
/// 발표/녹화 앱 활성화 감지에 쓴다(발표 안전). OUTOFCONTEXT라 콜백이 **호스트 스레드 메시지 큐**로 오므로
/// 후킹 설치도 콜백도 메시지 윈도우 호스트 스레드에서 일어난다.
/// </summary>
internal sealed class Win32ForegroundAppMonitor : IForegroundAppMonitor
{
    private readonly MessageWindowHost _host;
    private SN.WinEventProc? _proc; // GC 방지
    private IntPtr _hook;
    private bool _running;

    public ForegroundApp Current { get; private set; }
    public event Action<ForegroundApp>? Changed;

    public Win32ForegroundAppMonitor(MessageWindowHost host) => _host = host;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _host.Invoke(() =>
        {
            _proc = WinEventProc;
            _hook = SN.SetWinEventHook(
                SN.EVENT_SYSTEM_FOREGROUND, SN.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc,
                idProcess: 0, idThread: 0, SN.WINEVENT_OUTOFCONTEXT | SN.WINEVENT_SKIPOWNPROCESS);
        });
    }

    private void WinEventProc(IntPtr hHook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        var app = new ForegroundApp(ProcessName(hwnd), WindowTitle(hwnd));
        Current = app;
        Changed?.Invoke(app);
    }

    private static string ProcessName(IntPtr hwnd)
    {
        if (SN.GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return "";
        IntPtr h = SN.OpenProcess(SN.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return SN.QueryFullProcessImageName(h, 0, sb, ref size)
                ? Path.GetFileNameWithoutExtension(sb.ToString())
                : "";
        }
        finally { SN.CloseHandle(h); }
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        int len = SN.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        SN.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _host.Invoke(() =>
        {
            if (_hook != IntPtr.Zero) { SN.UnhookWinEvent(_hook); _hook = IntPtr.Zero; }
        });
    }

    public void Dispose() => Stop();
}
