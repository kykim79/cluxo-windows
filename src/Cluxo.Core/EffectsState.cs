namespace Cluxo.Core;

// 일시적 효과 — 각 효과는 ExpiresAt(초)을 들고, 렌더 루프가 매 프레임 Prune(now)로 만료분을 제거한다.
// (Swift EffectsState의 per-effect Task.sleep 자동 제거를 시간 주입 + 프레임 프루닝으로 대체:
//  결정적·테스트 가능, 타이머 난립 없음 — 렌더 루프 아키텍처에 부합.)
public readonly record struct ClickEffect(int Id, PointD Position, bool IsRight, bool IsDouble, double ExpiresAt);
public readonly record struct DoubleClickEffect(int Id, PointD Position, double ExpiresAt);
public readonly record struct ScrollEffect(
    int Id, PointD Position, bool IsPositive, bool IsVertical, double Magnitude, double ExpiresAt);
public readonly record struct ShakeEffect(int Id, PointD Position, double ExpiresAt);
public readonly record struct IdlePulseEffect(int Id, PointD Position, double ExpiresAt);
public readonly record struct TrailPoint(int Id, PointD Position);

/// <summary>
/// 일시적 효과 큐 (클릭/더블클릭/스크롤/흔들기/정지펄스/트레일). (Swift <c>EffectsState</c> 순수 이식)
///
/// 시간은 호출 측이 주입(now). animationSpeed도 인자로 받아 settings 결합 회피.
/// 트랙패드 제스처 효과는 v1 제외(Windows에 입력원 없음), 클립보드 효과는 v1 스코프 밖 — 보류.
/// </summary>
public sealed class EffectsState
{
    // 효과 수명(초). animationSpeed로 스케일. (Swift EffectsState의 Task.sleep 값과 동일)
    private const double ClickLife = 0.7;
    private const double DoubleClickLife = 0.9;
    private const double ScrollLife = 0.65;
    private const double ShakeLife = 1.8;
    private const double ShakeLifeFloor = 1.5;   // max(1.5, 1.8*speed)
    private const double IdlePulseLife = 0.9;    // 고정(animationSpeed 무관)
    private const int MaxTrail = 26;
    private const int MaxDragTrail = 14;          // 일반 trail보다 짧은 streak
    private const double DragFadeStep = 0.04;     // 드래그 종료 후 40ms당 1점 제거

    private int _nextId;
    private readonly List<ClickEffect> _clicks = new();
    private readonly List<DoubleClickEffect> _doubleClicks = new();
    private readonly List<ScrollEffect> _scrolls = new();
    private readonly List<ShakeEffect> _shakes = new();
    private readonly List<IdlePulseEffect> _idlePulses = new();
    private readonly List<TrailPoint> _trail = new();
    private readonly List<TrailPoint> _dragTrail = new();

    private double? _dragFadeStart;
    private int _dragFadeInitial;

    public IReadOnlyList<ClickEffect> Clicks => _clicks;
    public IReadOnlyList<DoubleClickEffect> DoubleClicks => _doubleClicks;
    public IReadOnlyList<ScrollEffect> Scrolls => _scrolls;
    public IReadOnlyList<ShakeEffect> Shakes => _shakes;
    public IReadOnlyList<IdlePulseEffect> IdlePulses => _idlePulses;
    public IReadOnlyList<TrailPoint> Trail => _trail;
    public IReadOnlyList<TrailPoint> DragTrail => _dragTrail;

    private int NextId() => ++_nextId;

    // MARK: Add

    public void AddClick(PointD point, bool isRight, bool isDouble, double now, double animationSpeed)
    {
        _clicks.Add(new ClickEffect(NextId(), point, isRight, isDouble, now + ClickLife * animationSpeed));
        if (isDouble)
            _doubleClicks.Add(new DoubleClickEffect(NextId(), point, now + DoubleClickLife * animationSpeed));
    }

    /// <summary>
    /// 스크롤 효과 추가. sameRegion(보통 커서가 속한 모니터 영역)을 주면 그 영역의 이전 스크롤만 제거 —
    /// 다중 모니터에서 다른 화면 효과는 유지. (Swift의 NSScreen 쿼리를 영역 주입으로 대체)
    /// </summary>
    public void AddScroll(PointD point, bool isPositive, bool isVertical, double magnitude,
        RectD? sameRegion, double now, double animationSpeed)
    {
        if (sameRegion is { } region)
            _scrolls.RemoveAll(e => region.Contains(e.Position));
        _scrolls.Add(new ScrollEffect(NextId(), point, isPositive, isVertical, magnitude,
            now + ScrollLife * animationSpeed));
    }

    public void AddShake(PointD point, double now, double animationSpeed)
        => _shakes.Add(new ShakeEffect(NextId(), point, now + Math.Max(ShakeLifeFloor, ShakeLife * animationSpeed)));

    public void AddIdlePulse(PointD point, double now)
        => _idlePulses.Add(new IdlePulseEffect(NextId(), point, now + IdlePulseLife));

    // MARK: Trail

    public void UpdateTrail(PointD point)
    {
        _trail.Add(new TrailPoint(NextId(), point));
        if (_trail.Count > MaxTrail) _trail.RemoveAt(0);
    }

    public void ClearTrail() => _trail.Clear();

    public void UpdateDragTrail(PointD point)
    {
        _dragFadeStart = null; // 새 드래그 샘플 → 진행 중 fade 취소
        _dragTrail.Add(new TrailPoint(NextId(), point));
        if (_dragTrail.Count > MaxDragTrail) _dragTrail.RemoveAt(0);
    }

    public void ClearDragTrail()
    {
        _dragTrail.Clear();
        _dragFadeStart = null;
    }

    /// <summary>드래그 종료 — 즉시 비우지 않고 Prune에서 40ms당 1점씩 점진 제거(fade out).</summary>
    public void BeginDragTrailFade(double now)
    {
        if (_dragTrail.Count == 0) { _dragFadeStart = null; return; }
        _dragFadeStart = now;
        _dragFadeInitial = _dragTrail.Count;
    }

    // MARK: Prune (프레임마다 호출 — 만료 효과 제거 + 드래그 trail fade 진행)

    public void Prune(double now)
    {
        _clicks.RemoveAll(e => e.ExpiresAt <= now);
        _doubleClicks.RemoveAll(e => e.ExpiresAt <= now);
        _scrolls.RemoveAll(e => e.ExpiresAt <= now);
        _shakes.RemoveAll(e => e.ExpiresAt <= now);
        _idlePulses.RemoveAll(e => e.ExpiresAt <= now);

        if (_dragFadeStart is { } start)
        {
            int removed = (int)((now - start) / DragFadeStep);
            int target = Math.Max(0, _dragFadeInitial - removed);
            while (_dragTrail.Count > target) _dragTrail.RemoveAt(0);
            if (_dragTrail.Count == 0) _dragFadeStart = null;
        }
    }
}
