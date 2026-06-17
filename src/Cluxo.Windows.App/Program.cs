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
        if (args.Length > 0 && args[0] == "--selftest-toolbar")
        {
            Environment.Exit(SelfTest.RunToolbar());
            return;
        }
        if (args.Length > 0 && args[0] == "--selftest-rings")
        {
            Environment.Exit(SelfTest.RunRings());
            return;
        }
        if (args.Length > 0 && args[0] == "--make-icon")
        {
            Ui.IconMaker.Make(args.Length > 1 ? args[1] : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cluxo.ico"));
            Environment.Exit(0);
            return;
        }
        if (args.Length > 0 && args[0] == "--settings")
        {
            // 트레이 없이 설정창만 단독 표시(편집은 %APPDATA% 영구화). 레이아웃 미리보기 + 독립 편집기.
            var app = new System.Windows.Application();
            using var fileStore = new JsonFileSettingsStore();
            var store = fileStore.Load();
            var settings = new CursorSettings(store);
            settings.Changed += () => fileStore.Save(store);
            app.Run(new Ui.SettingsWindow(settings, new RegistryLaunchAtLogin()));
            Environment.Exit(0);
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

        // 진단(--diag): 키 VK·모디파이어, 라디얼 chord 발화, 후킹 분실을 %TEMP%\cluxo-diag.log에 기록.
        if (args.Contains("--diag"))
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cluxo-diag.log");
            System.IO.File.WriteAllText(logPath, $"diag start {DateTime.Now:HH:mm:ss}\n");
            void Log(string s) { try { System.IO.File.AppendAllText(logPath, $"{DateTime.Now:HH:mm:ss.fff} {s}\n"); } catch { } }
            input.EnableKeyDiag((vk, down, mods) => Log($"KEY vk=0x{vk:X2} {(down ? "DN" : "up")} mods={mods}"));
            input.RadialTrigger.Opened += () => Log("=== RADIAL OPENED ===");
            input.RadialTrigger.Closed += () => Log("=== RADIAL CLOSED ===");
            coordinator.MouseHookLost += () => Log("HOOK REMOVED (reinstalled)");
        }

        // 핫키 등록 결과를 시작 시 기록(키 입력 불필요) — ⌃⌥D 등이 다른 앱과 충돌해 비활성됐는지 확인용.
        try
        {
            var hkLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cluxo-hotkeys.log");
            var failed = coordinator.FailedHotkeys;
            System.IO.File.WriteAllText(hkLog,
                $"{DateTime.Now:HH:mm:ss} hotkey registration\n" +
                (failed.Count == 0
                    ? "all Ctrl+Alt hotkeys registered OK (D,I,S,M,K,C,H,1-7)\n"
                    : $"FAILED (다른 앱이 선점): {string.Join(", ", failed)}\n"));
        }
        catch { }

        // 진단 — 그리기 모드 토글이 실제로 호출되는지 기록(⌃⌥D / 트레이 / ✕).
        coordinator.DrawingModeChanged += active =>
        {
            try
            {
                var p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cluxo-draw.log");
                System.IO.File.AppendAllText(p, $"{DateTime.Now:HH:mm:ss.fff} drawing={(active ? "ON" : "OFF")}\n");
            }
            catch { }
        };

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
            // 그리기/라디얼 모드: 오버레이 클릭통과를 끄고(창이 클릭 수신) + LL 훅이 좌·우 버튼을 흡수해
            // 아래 창/콘텐츠로 새지 않게 한다. 매 프레임 평가되므로 두 경로가 항상 동기화.
            capturesInput: () =>
            {
                bool c = coordinator.IsDrawingModeActive || coordinator.IsRadialMenuActive;
                input.CaptureMouseButtons = c;
                return c;
            });

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
