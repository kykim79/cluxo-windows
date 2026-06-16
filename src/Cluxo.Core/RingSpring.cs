namespace Cluxo.Core;

/// <summary>
/// 커서 링 스케일 스프링(설계 §5.2) — 클릭 시 squash(0.75)했다가 1.0으로 springy 복귀.
/// SwiftUI <c>spring(response,dampingFraction)</c>을 stiffness/damping으로 변환해 적분한다.
/// 순수 로직(렌더가 dt로 구동) → 테스트 가능, D2D 교체 시도 재사용.
/// </summary>
public sealed class RingSpring
{
    private const double Rest = 1.0;
    private readonly double _k; // stiffness  = ω₀²
    private readonly double _c; // damping    = 2ζω₀
    private double _value = Rest;
    private double _velocity;

    public double Value => _value;

    public RingSpring(Spring spring)
    {
        double w0 = 2 * Math.PI / spring.Response; // 자연 각진동수
        _k = w0 * w0;
        _c = 2 * spring.DampingFraction * w0;
    }

    /// <summary>클릭 squash — 스케일을 target(기본 0.75)로 떨어뜨린다. 이후 <see cref="Advance"/>가 1.0으로 복귀.</summary>
    public void Bump(double target = 0.75)
    {
        _value = target;
        _velocity = 0;
    }

    /// <summary>dt(초)만큼 스프링 적분(rest=1.0 수렴). 큰 dt에서도 안정하게 작은 스텝으로 분할.</summary>
    public void Advance(double dt)
    {
        if (dt <= 0) return;
        const double maxStep = 1.0 / 120;
        while (dt > 0)
        {
            double h = Math.Min(maxStep, dt);
            double accel = -_k * (_value - Rest) - _c * _velocity; // semi-implicit Euler
            _velocity += accel * h;
            _value += _velocity * h;
            dt -= h;
        }
    }
}
