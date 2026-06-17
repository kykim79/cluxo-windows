using System.Runtime.InteropServices;
using Cluxo.Core.Platform;
using IN = Cluxo.Windows.App.Input.NativeMethods;
using SN = Cluxo.Windows.App.Shell.ShellNativeMethods;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="ITrayIcon"/> — Shell_NotifyIcon + TrackPopupMenu(SHELL-LAYER.md §5). 작업표시줄 미노출.
/// 우클릭 시 컨텍스트 메뉴를 띄우고, 선택 항목 Id를 <see cref="ItemClicked"/>로 알린다.
/// 모든 트레이/메뉴 호출은 메시지 윈도우 호스트 스레드에서 일어난다(콜백이 그 스레드 큐로 옴).
/// </summary>
internal sealed class Win32TrayIcon : ITrayIcon
{
    private const uint TrayId = 1;
    private readonly MessageWindowHost _host;
    private readonly string _tooltip;
    private volatile IReadOnlyList<TrayMenuItem> _items = Array.Empty<TrayMenuItem>();
    private bool _added;
    private bool _disposed;
    private IntPtr _icon; // 파일에서 로드한 아이콘(소유 → DestroyIcon)

    public event Action<string>? ItemClicked;
    public event Action? IconClicked;

    /// <summary>설정 시 메뉴를 열 때마다 현재 상태로 항목을 새로 빌드(체크 표시 갱신용). 없으면 <see cref="SetMenu"/> 정적 항목.</summary>
    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public Win32TrayIcon(MessageWindowHost host, string tooltip = "Cluxo")
    {
        _host = host;
        _tooltip = tooltip;
        _host.Message += OnHostMessage;
        _host.Invoke(AddIcon);
    }

    private void AddIcon()
    {
        var nid = new SN.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<SN.NOTIFYICONDATA>(),
            hWnd = _host.Hwnd,
            uID = TrayId,
            uFlags = SN.NIF_MESSAGE | SN.NIF_ICON | SN.NIF_TIP,
            uCallbackMessage = SN.WM_TRAYCALLBACK,
            hIcon = LoadTrayIcon(), // Assets\cluxo.ico (없으면 시스템 기본)
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        _added = SN.Shell_NotifyIcon(SN.NIM_ADD, ref nid);
    }

    // 트레이 아이콘 로드 순서: ① Assets\cluxo.ico(코브랜딩 교체 가능) → ② exe에 박힌 아이콘
    // (단일파일 빌드는 Assets가 디스크에 없으므로) → ③ 시스템 기본.
    private IntPtr LoadTrayIcon()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "cluxo.ico");
            if (System.IO.File.Exists(path))
            {
                _icon = SN.LoadImage(IntPtr.Zero, path, SN.IMAGE_ICON, 0, 0, SN.LR_LOADFROMFILE | SN.LR_DEFAULTSIZE);
                if (_icon != IntPtr.Zero) return _icon;
            }

            // exe에 임베드된 아이콘(ApplicationIcon) — 단일파일 배포에서 항상 동작.
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe) && SN.ExtractIconEx(exe, 0, out var large, out var small, 1) > 0)
            {
                _icon = small != IntPtr.Zero ? small : large;
                var other = small != IntPtr.Zero ? large : IntPtr.Zero;
                if (other != IntPtr.Zero) SN.DestroyIcon(other); // 안 쓰는 핸들 정리
                if (_icon != IntPtr.Zero) return _icon;
            }
        }
        catch { /* 폴백 */ }
        return SN.LoadIcon(IntPtr.Zero, SN.IDI_APPLICATION);
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items) => _items = items;

    private void OnHostMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != SN.WM_TRAYCALLBACK) return;
        uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
        if (mouseMsg is SN.WM_RBUTTONUP or SN.WM_CONTEXTMENU)
            ShowMenu(); // 호스트 스레드에서 실행 중
        else if (mouseMsg == SN.WM_LBUTTONUP)
            IconClicked?.Invoke(); // 좌클릭 — 활성/비활성 토글(맥 대응)
    }

    /// <summary>풍선 알림(예: 마우스 후킹 재설치 T2). 호스트 스레드로 마샬링.</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_added) return;
        _host.Post(() =>
        {
            var nid = new SN.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<SN.NOTIFYICONDATA>(),
                hWnd = _host.Hwnd,
                uID = TrayId,
                uFlags = SN.NIF_INFO,
                szInfo = text,
                szInfoTitle = title,
                szTip = _tooltip,
                dwInfoFlags = SN.NIIF_INFO,
            };
            SN.Shell_NotifyIcon(SN.NIM_MODIFY, ref nid);
        });
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke() ?? _items;
        if (items.Count == 0) return;

        var flat = new List<TrayMenuItem>();   // cmd id(1-based) → 클릭 가능한 항목(서브메뉴 부모 제외)
        IntPtr root = BuildMenu(items, flat);
        try
        {
            IN.GetCursorPos(out var pt);
            SN.SetForegroundWindow(_host.Hwnd); // 메뉴가 바깥 클릭에 안 닫히는 버그 회피
            int cmd = SN.TrackPopupMenu(root, SN.TPM_RETURNCMD | SN.TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, _host.Hwnd, IntPtr.Zero);
            if (cmd > 0 && cmd <= flat.Count) ItemClicked?.Invoke(flat[cmd - 1].Id);
        }
        finally
        {
            SN.DestroyMenu(root); // 서브메뉴 HMENU도 함께 파기됨
        }
    }

    // 메뉴/서브메뉴를 재귀로 만든다. 클릭 가능한 항목은 flat에 1-based cmd id로 누적.
    private static IntPtr BuildMenu(IReadOnlyList<TrayMenuItem> items, List<TrayMenuItem> flat)
    {
        IntPtr menu = SN.CreatePopupMenu();
        foreach (var it in items)
        {
            if (it.IsSeparatorBefore) SN.AppendMenu(menu, SN.MF_SEPARATOR, UIntPtr.Zero, null);
            if (it.Submenu is { Count: > 0 } sub)
            {
                IntPtr child = BuildMenu(sub, flat);
                uint pflags = SN.MF_STRING | SN.MF_POPUP | (it.IsEnabled ? 0u : SN.MF_GRAYED);
                SN.AppendMenu(menu, pflags, (UIntPtr)(long)child, it.Label);
            }
            else
            {
                flat.Add(it);
                uint flags = SN.MF_STRING
                    | (it.IsChecked ? SN.MF_CHECKED : 0u)
                    | (it.IsEnabled ? 0u : SN.MF_GRAYED);
                SN.AppendMenu(menu, flags, (UIntPtr)flat.Count, it.Label); // cmd id = 누적 순서(0=선택없음)
            }
        }
        return menu;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Message -= OnHostMessage;
        if (_added)
        {
            _host.Invoke(() =>
            {
                var nid = new SN.NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<SN.NOTIFYICONDATA>(),
                    hWnd = _host.Hwnd,
                    uID = TrayId,
                    szTip = "", szInfo = "", szInfoTitle = "",
                };
                SN.Shell_NotifyIcon(SN.NIM_DELETE, ref nid);
            });
        }
        if (_icon != IntPtr.Zero) { SN.DestroyIcon(_icon); _icon = IntPtr.Zero; }
    }
}
