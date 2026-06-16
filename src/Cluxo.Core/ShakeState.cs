namespace Cluxo.Core;

/// <summary>
/// 마우스 흔들기(SOS) 감지를 위한 순수 상태 + 알고리즘. (Swift <c>ShakeState</c> 이식)
///
/// 시간을 <paramref name="at"/> 인자로 받아 wall clock 없이 테스트에서 시뮬레이션 가능.
///
/// 감지 방식 — 각 축 독립 추적 + dedup:
///   - vx와 vy를 별도 카운터로 추적
///   - 각 축: |v| &gt; 150 + 이전 |v| &gt; 150 + 부호 반대일 때 방향 전환 카운트
///   - 0.5초 안에 한 축에서 방향 전환 requiredDirChanges회 누적되면 detect 후보
///   - dedup: 직전 detect로부터 0.5초 안에는 재발화 X (같은 흔들기 두 축 동시 trigger 회피)
/// </summary>
public sealed class ShakeState
{
    // 일반 마우스 이동과 흔들기 구분 — 속도가 아니라 "방향 전환 횟수"가 주된 신호.
    private const double VelocityThreshold = 150; // 움직임으로 인정하는 최소 속도 (잡음 배제)
    private const double LastVThreshold = 50;     // lastV 업데이트 임계 (잡음 무시)
    private const double DedupWindow = 0.5;       // detect 후 다음 detect까지 최소 간격
    private const double Window = 0.5;            // recent 보관 윈도

    private readonly record struct PosRecord(double X, double Y, double T);

    /// <summary>한 축(x 또는 y)의 진동 추적 상태.</summary>
    private sealed class AxisState
    {
        private double _lastV;
        private double _lastChangeTime;
        public int DirChanges;

        /// <summary>새 속도를 기록. required회 방향 전환 누적 시 true.</summary>
        public bool Update(double v, double now, int required)
        {
            bool detected = false;
            if (Math.Abs(v) > VelocityThreshold &&
                Math.Abs(_lastV) > VelocityThreshold &&
                SignValue(v) != SignValue(_lastV))
            {
                DirChanges = (now - _lastChangeTime < 0.5) ? DirChanges + 1 : 1;
                _lastChangeTime = now;
                if (DirChanges >= required) detected = true;
            }
            if (Math.Abs(v) > LastVThreshold) _lastV = v;
            return detected;
        }
    }

    private readonly List<PosRecord> _recent = new();
    private readonly AxisState _axisX = new();
    private readonly AxisState _axisY = new();
    private double _lastDetectionTime = -1; // dedup용 (-1 = 아직 detect 없음)

    /// <summary>감지에 필요한 방향 전환 횟수(민감도). 적을수록 민감. default "보통".</summary>
    public int RequiredDirChanges { get; set; } = 5;

    /// <summary>
    /// 새 좌표·시각을 기록하고 흔들기 감지 여부를 반환.
    /// 감지 시 두 축 모두 카운터 리셋 + dedup 타임스탬프 갱신.
    /// </summary>
    public bool Record(double x, double y, double at)
    {
        double now = at;
        _recent.Add(new PosRecord(x, y, now));
        while (_recent.Count > 0 && now - _recent[0].T >= Window) _recent.RemoveAt(0);
        if (_recent.Count < 2) return false;

        var prev = _recent[^2];
        var curr = _recent[^1];
        double dt = curr.T - prev.T;
        if (dt <= 0.001) return false;

        double vx = (curr.X - prev.X) / dt;
        double vy = (curr.Y - prev.Y) / dt;

        // 각 축 독립 detect — 둘 다 호출(단락 평가 금지: 두 축 상태 모두 갱신돼야 함)
        bool xDetected = _axisX.Update(vx, now, RequiredDirChanges);
        bool yDetected = _axisY.Update(vy, now, RequiredDirChanges);
        if (!(xDetected || yDetected)) return false;

        // dedup — 직전 detect 후 0.5초 안엔 무시
        if (_lastDetectionTime >= 0 && now - _lastDetectionTime < DedupWindow) return false;
        _lastDetectionTime = now;
        // 다음 흔들기는 처음부터 누적 → 두 축 카운터 리셋
        _axisX.DirChanges = 0;
        _axisY.DirChanges = 0;
        return true;
    }

    /// <summary>0 또는 양수면 +1, 음수면 -1 (Swift signValue와 동일 의미).</summary>
    private static int SignValue(double v) => v >= 0 ? 1 : -1;
}
