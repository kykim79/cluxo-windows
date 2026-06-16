using Cluxo.Core;
using Cluxo.Core.Platform;
using Cluxo.Windows.App.Input;
using Cluxo.Windows.App.Render;
using Cluxo.Windows.App.Shell;

namespace Cluxo.Windows.App;

/// <summary>
/// 진입점 — 입력·Shell·렌더 계층을 <see cref="OverlayCoordinator"/>에 주입하고 구동한다.
/// (INPUT-LAYER.md §6 / OVERLAY-RENDER.md §3)
///
/// 스레드: 입력(LL 후킹 스레드)·Shell(메시지 윈도우 STA)·렌더(WPF STA)·핫키(전용 스레드)가
/// 각자 펌프. 메인 스레드는 트레이 '종료'까지 대기만 한다.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var input = new Win32InputLayer();
        using var shell = new Win32ShellLayer("Cluxo");
        using var overlay = new WpfOverlayHost(() => shell.Clock.NowSeconds);

        var coordinator = new OverlayCoordinator(
            input.Mouse, input.Keyboard, input.Hotkeys, input.CursorSource, shell.Monitors,
            overlay.Factory, shell.Settings, shell.Branding, shell.Foreground, input.RadialTrigger, shell.Clock);

        using var exit = new ManualResetEventSlim(false);
        coordinator.MouseHookLost += () => { /* TODO: 트레이 풍선 알림(후킹 재설치됨) */ };

        shell.Tray.SetMenu(new[]
        {
            new TrayMenuItem("drawing", "그리기 모드: Ctrl+Alt+D", IsEnabled: false),
            new TrayMenuItem("inspector", "좌표 표시: Ctrl+Alt+I", IsEnabled: false),
            new TrayMenuItem("quit", "종료", IsSeparatorBefore: true),
        });
        shell.Tray.ItemClicked += id => { if (id == "quit") exit.Set(); };

        coordinator.Start();
        overlay.StartRenderLoop(
            coordinator.RenderFrame,
            capturesInput: () => coordinator.IsDrawingModeActive || coordinator.IsRadialMenuActive);

        exit.Wait(); // 트레이 '종료' 선택까지 블록

        // 종료 순서: 렌더 루프 정지 → 코디네이터 → 렌더 호스트 (UI 스레드가 _gate 다투지 않게)
        overlay.StopRenderLoop();
        coordinator.Dispose();
        // using: overlay → shell → input 순으로 Dispose
    }
}
