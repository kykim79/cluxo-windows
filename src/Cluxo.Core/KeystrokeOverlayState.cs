namespace Cluxo.Core;

/// <summary>
/// 화면 하단 키스트로크 오버레이 + 상태 알림(토글 안내). (Swift <c>KeystrokeOverlayState</c> 이식)
///
/// 표시 후 일정 시간 뒤 자동 숨김 — Swift의 Task.sleep을 시간 주입 <see cref="Tick"/>으로 대체
/// (결정적·테스트 가능). timeout은 호출 측이 주입(settings 결합 회피). 페이드는 렌더 계층이 적용.
/// </summary>
public sealed class KeystrokeOverlayState
{
    /// <summary>상태 알림 고정 표시 시간(초) — 단축키 토글 등 짧은 안내.</summary>
    public const double StatusNotificationTimeout = 1.5;

    public string KeystrokeText { get; private set; } = "";
    public bool IsVisible { get; private set; }

    private double _hideAt = double.PositiveInfinity;

    /// <summary>키스트로크 표시 — timeout 초 후 <see cref="Tick"/>에서 자동 숨김. 재호출 시 타이머 리셋.</summary>
    public void ShowKeystroke(string text, double timeout, double now)
    {
        KeystrokeText = text;
        IsVisible = true;
        _hideAt = now + timeout;
    }

    /// <summary>상태 알림(1.5초 고정).</summary>
    public void ShowStatusNotification(string text, double now)
        => ShowKeystroke(text, StatusNotificationTimeout, now);

    /// <summary>매 프레임 호출 — 만료 시 숨김(텍스트는 유지, 렌더가 페이드).</summary>
    public void Tick(double now)
    {
        if (IsVisible && now >= _hideAt)
            IsVisible = false;
    }
}
