using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Input;

/// <summary>
/// <see cref="ICursorPositionSource"/> — GetCursorPos. 렌더 루프가 vsync마다 폴링한다.
/// 하이브리드 입력(설계 발견1): 이동은 후킹하지 않고 프레임마다 샘플한다.
///
/// 반환은 물리 픽셀(가상 데스크톱 좌표). PerMonitorV2 DPI 인식 하에 모니터별 스케일 변환은
/// 렌더 계층이 담당(좌표는 그대로 전달).
/// </summary>
internal sealed class CursorPositionSource : ICursorPositionSource
{
    public PointD GetCursorPosition()
        => NativeMethods.GetCursorPos(out var p)
            ? new PointD(p.X, p.Y)
            : PointD.Zero;
}
