using System.Runtime.InteropServices;
using Cluxo.Core;
using Cluxo.Core.Platform;
using SN = Cluxo.Windows.App.Shell.ShellNativeMethods;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="IMonitorProvider"/> — EnumDisplayMonitors + Per-Monitor DPI(SHELL-LAYER.md §7).
/// WM_DISPLAYCHANGE/WM_DPICHANGED(메시지 윈도우)에서 재열거 후 <see cref="MonitorsChanged"/>.
///
/// 주의(설계 §7): 안정 Id는 v1에서 szDevice(\\.\DISPLAY1)로 시작 — 재배열 시 바뀔 수 있다.
/// 신뢰 모니터(발표 안전) 매칭이 어긋나지 않게 **출시 전 DisplayConfig 기반 id로 교체** 필요.
/// </summary>
internal sealed class Win32MonitorProvider : IMonitorProvider, IDisposable
{
    private readonly MessageWindowHost _host;
    private readonly object _gate = new();
    private IReadOnlyList<MonitorInfo> _monitors;

    public event Action? MonitorsChanged;

    public Win32MonitorProvider(MessageWindowHost host)
    {
        _host = host;
        _monitors = Enumerate();
        _host.Message += OnHostMessage;
    }

    public IReadOnlyList<MonitorInfo> Monitors
    {
        get { lock (_gate) return _monitors; }
    }

    private void OnHostMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != SN.WM_DISPLAYCHANGE && msg != SN.WM_DPICHANGED) return;
        var fresh = Enumerate();
        lock (_gate) _monitors = fresh;
        MonitorsChanged?.Invoke();
    }

    /// <summary>현재 모니터 구성 열거. (Dispose 불필요 — 동기 호출)</summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();

        bool Callback(IntPtr hMon, IntPtr hdc, ref SN.RECT rc, IntPtr data)
        {
            var mi = new SN.MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<SN.MONITORINFOEX>() };
            if (SN.GetMonitorInfo(hMon, ref mi))
            {
                double scale = 1.0;
                if (SN.GetDpiForMonitor(hMon, SN.MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0)
                    scale = dx / 96.0;
                var b = mi.rcMonitor;
                list.Add(new MonitorInfo(
                    Id: mi.szDevice,
                    Bounds: new RectD(b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top),
                    DpiScale: scale,
                    IsPrimary: (mi.dwFlags & SN.MONITORINFOF_PRIMARY) != 0));
            }
            return true; // 계속 열거
        }

        SN.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return list;
    }

    public void Dispose() => _host.Message -= OnHostMessage;
}
