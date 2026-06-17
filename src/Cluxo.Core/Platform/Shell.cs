namespace Cluxo.Core.Platform;

/// <summary>
/// 설정 영구화 — %APPDATA%/Cluxo/settings.json. 직렬화 의미론은 <see cref="JsonSettingsStore"/>가,
/// 파일 IO와 디바운스(쓰기 코얼레싱)는 플랫폼 구현이 담당.
/// </summary>
public interface ISettingsStore
{
    /// <summary>디스크에서 로드. 파일 없거나 손상이면 빈 store(기본값 fallback).</summary>
    JsonSettingsStore Load();

    /// <summary>저장 요청. 구현은 디바운스 후 파일에 쓴다(슬라이더 등 잦은 쓰기 코얼레싱).</summary>
    void Save(JsonSettingsStore store);
}

/// <summary>
/// 코브랜딩 제공 — 에셋을 무결성 검증(<see cref="BrandingLoader"/>)해 로드. 실패 시 순정 Default.
/// 렌더/설정 UI가 Current를 읽어 로고·회사명·accent를 적용.
/// </summary>
public interface IBrandingProvider
{
    BrandingConfig Current { get; }
}

/// <summary>로그인 시 자동 실행 (레지스트리 Run / 작업 스케줄러).</summary>
public interface ILaunchAtLogin
{
    bool IsEnabled { get; set; }
}

/// <summary>트레이 메뉴 항목 한 줄. IsSeparatorBefore=true면 이 항목 위에 구분선.
/// Submenu가 있으면 하위 메뉴(▶)로 펼쳐진다(이 항목 자체는 클릭 동작 없음).</summary>
public readonly record struct TrayMenuItem(
    string Id, string Label, bool IsChecked = false, bool IsEnabled = true, bool IsSeparatorBefore = false,
    IReadOnlyList<TrayMenuItem>? Submenu = null);

/// <summary>시스템 트레이 아이콘 + 메뉴. (Shell_NotifyIcon, 작업표시줄 미노출)</summary>
public interface ITrayIcon : IDisposable
{
    /// <summary>메뉴 항목 설정(상태 변경 시 다시 호출해 체크/활성 갱신).</summary>
    void SetMenu(IReadOnlyList<TrayMenuItem> items);

    /// <summary>항목 클릭 — 인자는 항목 Id.</summary>
    event Action<string>? ItemClicked;

    /// <summary>트레이 아이콘 좌클릭 — 맥처럼 활성/비활성 토글에 쓴다.</summary>
    event Action? IconClicked;
}

/// <summary>포그라운드 앱 식별 — 발표/녹화 앱 감지에 사용.</summary>
public readonly record struct ForegroundApp(string ProcessName, string WindowTitle);

/// <summary>
/// 포그라운드 앱 변경 감시 (SetWinEventHook EVENT_SYSTEM_FOREGROUND + 프로세스명).
/// 발표·녹화 앱이 활성화되면 키스트로크 표시 등을 켜는 쪽으로(발표 안전).
/// </summary>
public interface IForegroundAppMonitor : IDisposable
{
    ForegroundApp Current { get; }
    event Action<ForegroundApp>? Changed;
    void Start();
    void Stop();
}
