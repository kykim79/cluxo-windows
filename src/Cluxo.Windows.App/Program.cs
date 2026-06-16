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
    private static void Main(string[] args)
    {
        // 개발/QA 셀프테스트 — 입력 합성이 막힌 환경에서 렌더·토글 검증(렌더만, 입력 후킹 X).
        if (args.Length > 0 && args[0] == "--selftest")
        {
            Environment.Exit(SelfTest.Run());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest-fx")
        {
            Environment.Exit(SelfTest.RunFx());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest-radial")
        {
            Environment.Exit(SelfTest.RunRadial());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest-settings")
        {
            Environment.Exit(SelfTest.RunSettings());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest-rings")
        {
            Environment.Exit(SelfTest.RunRings());
            return;
        }

        // 입력·Shell·렌더 계층은 서로 독립(각자 전용 스레드) → 병렬 생성으로 스레드 기동·WPF init을 겹친다.
        // overlay의 clock 람다는 렌더 시점에만 호출되므로 shell이 그때 준비돼 있으면 된다.
        Win32InputLayer input = null!;
        Win32ShellLayer shell = null!;
        WpfOverlayHost overlay = null!;
        Parallel.Invoke(
            () => input = new Win32InputLayer(),
            () => shell = new Win32ShellLayer("Cluxo"),
            () => overlay = new WpfOverlayHost(() => shell.Clock.NowSeconds));

        var coordinator = new OverlayCoordinator(
            input.Mouse, input.Keyboard, input.Hotkeys, input.CursorSource, shell.Monitors,
            overlay.Factory, shell.Settings, shell.Branding, shell.Foreground, input.RadialTrigger, shell.Clock);

        using var exit = new ManualResetEventSlim(false);

        // T2: 마우스 후킹 분실 → 재설치됨을 트레이 풍선으로 알림.
        coordinator.MouseHookLost += () => shell.ShowTrayBalloon("Cluxo", "마우스 후킹이 재설치되었습니다.");

        // 트레이 메뉴 — 열 때마다 현재 상태로 체크 표시 갱신.
        IReadOnlyList<TrayMenuItem> BuildMenu() => new[]
        {
            new TrayMenuItem("drawing", "그리기 모드", IsChecked: coordinator.IsDrawingModeActive),
            new TrayMenuItem("inspector", "좌표 표시", IsChecked: coordinator.IsInspectorActive),
            new TrayMenuItem("settings", "설정...", IsSeparatorBefore: true),
            new TrayMenuItem("quit", "종료", IsSeparatorBefore: true),
        };
        shell.SetTrayMenuProvider(BuildMenu);
        shell.Tray.SetMenu(BuildMenu()); // 초기 항목(폴백)
        shell.Tray.ItemClicked += id =>
        {
            switch (id)
            {
                case "drawing": coordinator.ToggleDrawingMode(); break;
                case "inspector": coordinator.ToggleInspector(); break;
                case "settings": overlay.ShowSettings(coordinator.Settings, shell.LaunchAtLogin); break;
                case "quit": exit.Set(); break;
            }
        };

        coordinator.Start();
        overlay.StartRenderLoop(
            coordinator.RenderFrame,
            capturesInput: () => coordinator.IsDrawingModeActive || coordinator.IsRadialMenuActive);

        // 테스트용 자동 종료 — 프로덕션 종료 경로를 그대로 타서 검증(--exit-after-ms N).
        using var autoExit = ScheduleAutoExit(args, exit);

        exit.Wait(); // 트레이 '종료'(또는 자동 종료)까지 블록

        // 명시적 순서 종료(using 역순 의존보다 명확): 렌더 루프 정지 → 코디네이터 → 렌더 호스트 → Shell → 입력.
        // 렌더 루프를 먼저 멈춰야 UI 스레드가 coordinator._gate를 다투지 않는다. 정상 종료는 ~수십 ms.
        overlay.StopRenderLoop();
        coordinator.Dispose();
        overlay.Dispose();
        shell.Dispose();
        input.Dispose();
    }

    private static IDisposable? ScheduleAutoExit(string[] args, ManualResetEventSlim exit)
    {
        int i = Array.IndexOf(args, "--exit-after-ms");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int ms) && ms > 0)
            return new Timer(_ => exit.Set(), null, ms, Timeout.Infinite);
        return null;
    }
}
