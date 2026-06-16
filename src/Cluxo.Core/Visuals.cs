namespace Cluxo.Core;

/// <summary>스프링 애니메이션 파라미터(플랫폼 무관). 렌더 계층이 네이티브 애니메이션으로 변환.
/// (SwiftUI <c>Animation.spring(response:dampingFraction:)</c> 대응)</summary>
public readonly record struct Spring(double Response, double DampingFraction);

/// <summary>이징 곡선 종류.</summary>
public enum EaseCurve { Out, InOut }

/// <summary>이징 애니메이션 파라미터. (SwiftUI <c>Animation.easeOut/easeInOut(duration:)</c> 대응)</summary>
public readonly record struct Ease(EaseCurve Curve, double Duration);

/// <summary>폰트 두께.</summary>
public enum FontWeight { Regular, Medium, Semibold }

/// <summary>폰트 토큰(플랫폼 무관). 렌더 계층이 시스템 폰트로 매핑. (SwiftUI <c>Font.system</c> 대응)</summary>
public readonly record struct FontToken(double Size, FontWeight Weight = FontWeight.Regular, bool Monospaced = false);
