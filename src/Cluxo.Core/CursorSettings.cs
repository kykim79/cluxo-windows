namespace Cluxo.Core;

/// <summary>
/// 영구 사용자 설정 — <see cref="JsonSettingsStore"/> 백업 타입 접근자. (Swift <c>CursorSettings</c> 이식)
///
/// 키·기본값은 Swift와 동일. 디바운스/파일 저장은 플랫폼 IO 계층(ISettingsStore.Save)이 담당 —
/// 여기서는 Set 시 <see cref="Changed"/>를 발생시켜 코디네이터가 저장을 스케줄하게 한다.
///
/// 제외(별도/다른 계층): macOS VK 키코드(Windows는 HotkeyChord로 재설계), 로그인 시 실행(ILaunchAtLogin),
/// 라디얼 메뉴 트리(RadialMenuItem — UI 콘텐츠, 별도 이식). 돋보기 설정은 v1.1이지만 값은 보관.
/// </summary>
public sealed class CursorSettings
{
    private readonly JsonSettingsStore _store;

    /// <summary>설정 변경 시 발생 — 코디네이터가 ISettingsStore.Save를 디바운스 스케줄.</summary>
    public event Action? Changed;

    public CursorSettings(JsonSettingsStore store) => _store = store;

    private T Get<T>(string key, T def) => _store.Get(key, def);
    private void Set<T>(string key, T value) { _store.Set(key, value); Changed?.Invoke(); }

    // ── 링 외형 ──────────────────────────────────────────────────
    public RingColor RingColor { get => Get("ringColor", RingColor.Cyan); set => Set("ringColor", value); }
    public RingShape RingShape { get => Get("ringShape", RingShape.Circle); set => Set("ringShape", value); }
    public RingSize RingSize { get => Get("ringSize", RingSize.Medium); set => Set("ringSize", value); }
    public double RingOpacity { get => Get("ringOpacity", 1.0); set => Set("ringOpacity", value); }
    public BorderWeight BorderWeight { get => Get("borderWeight", BorderWeight.Thin); set => Set("borderWeight", value); }
    public BorderStyle BorderStyle { get => Get("borderStyle", BorderStyle.Solid); set => Set("borderStyle", value); }
    public bool IsPerspectiveWarping { get => Get("perspectiveWarping", false); set => Set("perspectiveWarping", value); }
    public bool HasInnerRing { get => Get("hasInnerRing", false); set => Set("hasInnerRing", value); }
    public bool IsRingFillEnabled { get => Get("isRingFillEnabled", true); set => Set("isRingFillEnabled", value); }

    // ── 모션/타이밍 ──────────────────────────────────────────────
    public AnimationSpeed AnimationSpeed { get => Get("animationSpeed", AnimationSpeed.Normal); set => Set("animationSpeed", value); }
    public double KeystrokeTimeout { get => Get("keystrokeTimeout", 3.0); set => Set("keystrokeTimeout", value); }
    public double IdleTimeout { get => Get("idleTimeout", 3.0); set => Set("idleTimeout", value); }

    // ── 효과 토글 (v1.0 minimalist default) ──────────────────────
    public bool IsGlowEnabled { get => Get("isGlowEnabled", false); set => Set("isGlowEnabled", value); }
    public bool IsKeystrokeEnabled { get => Get("isKeystrokeEnabled", false); set => Set("isKeystrokeEnabled", value); }
    public bool IsTrailEnabled { get => Get("isTrailEnabled", false); set => Set("isTrailEnabled", value); }
    public bool IsAnchoredLineEnabled { get => Get("isAnchoredLineEnabled", false); set => Set("isAnchoredLineEnabled", value); }
    public bool IsCometTailEnabled { get => Get("isCometTailEnabled", false); set => Set("isCometTailEnabled", value); }
    public bool IsDragAngleLabelEnabled { get => Get("isDragAngleLabelEnabled", false); set => Set("isDragAngleLabelEnabled", value); }
    public bool IsIdlePulseEnabled { get => Get("isIdlePulseEnabled", true); set => Set("isIdlePulseEnabled", value); }
    public bool IsScrollIndicatorEnabled { get => Get("scrollIndicator", true); set => Set("scrollIndicator", value); }
    public bool IsShakeEnabled { get => Get("isShakeEnabled", true); set => Set("isShakeEnabled", value); }
    public ShakeSensitivity ShakeSensitivity { get => Get("shakeSensitivity", ShakeSensitivity.Normal); set => Set("shakeSensitivity", value); }
    public bool RightClickUsesRingColor { get => Get("rightClickUsesRingColor", false); set => Set("rightClickUsesRingColor", value); }

    // ── 스포트라이트 / 돋보기(v1.1 값 보관) ──────────────────────
    public double SpotlightRadius { get => Get("spotlightRadius", 130.0); set => Set("spotlightRadius", value); }
    public double SpotlightEdgeSoftness { get => Get("spotlightEdgeSoftness", 0.4); set => Set("spotlightEdgeSoftness", value); }
    public double MagnifierZoom { get => Get("magnifierZoom", 2.0); set => Set("magnifierZoom", value); }
    public double MagnifierSize { get => Get("magnifierSize", 200.0); set => Set("magnifierSize", value); }

    // ── 단축키 (키 부분만 — 모디파이어는 Ctrl+Alt 고정). HotkeyChord 키 문자열, 기본은 맥과 동일. ──
    public string HotkeyDrawing { get => Get("hotkey.drawing", "D"); set => Set("hotkey.drawing", value); }
    public string HotkeyInspector { get => Get("hotkey.inspector", "I"); set => Set("hotkey.inspector", value); }
    public string HotkeySpotlight { get => Get("hotkey.spotlight", "S"); set => Set("hotkey.spotlight", value); }
    public string HotkeyMagnifier { get => Get("hotkey.magnifier", "M"); set => Set("hotkey.magnifier", value); }
    public string HotkeyKeystroke { get => Get("hotkey.keystroke", "K"); set => Set("hotkey.keystroke", value); }

    // ── 그리기 toolbar 위치 ──────────────────────────────────────
    public double DrawingToolbarLeading { get => Get("drawingToolbarLeading", 28.0); set => Set("drawingToolbarLeading", value); }
    public double DrawingToolbarBottom { get => Get("drawingToolbarBottom", 110.0); set => Set("drawingToolbarBottom", value); }

    // ── 발표 안전 ────────────────────────────────────────────────
    public bool AutoEnableOnRecording { get => Get("autoEnableOnRecording", false); set => Set("autoEnableOnRecording", value); }
    public bool AutoKeystrokeOnUnknownMonitor { get => Get("autoKeystrokeOnUnknownMonitor", false); set => Set("autoKeystrokeOnUnknownMonitor", value); }
    public PreferredLanguage PreferredLanguage { get => Get("preferredLanguage", PreferredLanguage.System); set => Set("preferredLanguage", value); }

    /// <summary>비영구 — 재시작 시 항상 false. 외부 캡처가 오버레이를 잡게 풀기.</summary>
    public bool IsScreenshotMode { get; set; }

    // ── 커스텀 색 + 유효 색 ──────────────────────────────────────
    public Rgba CustomRingColor { get => Get("customRingColor", new Rgba(255, 128, 0)); set => Set("customRingColor", value); }

    /// <summary>모든 Active 효과가 따르는 accent — Custom이면 customRingColor, 아니면 미리 정의된 색.</summary>
    public Rgba EffectiveRingColor => RingColor == RingColor.Custom ? CustomRingColor : RingColor.Color();

    // ── 신뢰 모니터 ──────────────────────────────────────────────
    public IReadOnlyList<string> TrustedMonitorUUIDs => Get("trustedMonitorUUIDs", new List<string>());

    public bool IsTrustedMonitor(string uuid) => TrustedMonitorUUIDs.Contains(uuid);

    public void SetTrusted(string uuid, bool trusted)
    {
        var list = new List<string>(TrustedMonitorUUIDs);
        if (trusted) { if (!list.Contains(uuid)) list.Add(uuid); }
        else list.RemoveAll(u => u == uuid);
        Set("trustedMonitorUUIDs", list);
    }
}
