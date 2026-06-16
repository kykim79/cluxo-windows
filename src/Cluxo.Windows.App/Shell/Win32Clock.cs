using System.Diagnostics;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="IClock"/> — Stopwatch 기반 monotonic 초(SHELL-LAYER.md §8).
/// wall clock이 아니라 시계 변경에 영향받지 않는다. ShakeState·EffectsState·KeystrokeOverlayState·
/// RadialMenuController·드래그 모션의 단일 시간 주입원.
/// </summary>
public sealed class Win32Clock : IClock
{
    public double NowSeconds => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
}
