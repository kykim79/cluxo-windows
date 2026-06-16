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

    public event Action<string>? ItemClicked;

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
            hIcon = SN.LoadIcon(IntPtr.Zero, SN.IDI_APPLICATION), // 코브랜딩 시 회사 .ico로 교체
            szTip = _tooltip,
            szInfo = "",
            szInfoTitle = "",
        };
        _added = SN.Shell_NotifyIcon(SN.NIM_ADD, ref nid);
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items) => _items = items;

    private void OnHostMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != SN.WM_TRAYCALLBACK) return;
        uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
        if (mouseMsg is SN.WM_RBUTTONUP or SN.WM_CONTEXTMENU)
            ShowMenu(); // 호스트 스레드에서 실행 중
    }

    private void ShowMenu()
    {
        var items = _items;
        if (items.Count == 0) return;

        IntPtr menu = SN.CreatePopupMenu();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it.IsSeparatorBefore) SN.AppendMenu(menu, SN.MF_SEPARATOR, UIntPtr.Zero, null);
                uint flags = SN.MF_STRING
                    | (it.IsChecked ? SN.MF_CHECKED : 0u)
                    | (it.IsEnabled ? 0u : SN.MF_GRAYED);
                SN.AppendMenu(menu, flags, (UIntPtr)(i + 1), it.Label); // cmd id = index+1 (0=선택없음)
            }

            IN.GetCursorPos(out var pt);
            SN.SetForegroundWindow(_host.Hwnd); // 메뉴가 바깥 클릭에 안 닫히는 버그 회피
            int cmd = SN.TrackPopupMenu(menu, SN.TPM_RETURNCMD | SN.TPM_RIGHTBUTTON,
                pt.X, pt.Y, 0, _host.Hwnd, IntPtr.Zero);
            if (cmd > 0 && cmd <= items.Count) ItemClicked?.Invoke(items[cmd - 1].Id);
        }
        finally
        {
            SN.DestroyMenu(menu);
        }
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
    }
}
