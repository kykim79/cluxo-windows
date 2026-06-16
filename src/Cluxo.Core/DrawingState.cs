namespace Cluxo.Core;

/// <summary>그리기 도구. (Swift <c>DrawingState.Tool</c>) badge는 클릭 즉시 commit + 자동 번호.</summary>
public enum DrawingTool { Pen, Arrow, Line, Rectangle, Ellipse, Highlighter, Badge }

/// <summary>한 도형. points: pen·highlighter는 모든 샘플, 그 외는 [start,end], badge는 [point].</summary>
public sealed class DrawingShape
{
    public Guid Id { get; } = Guid.NewGuid();
    public DrawingTool Tool { get; }
    public Rgba Color { get; }
    public double LineWidth { get; }            // startShape 시점 캡처 — 이후 변경 무영향
    public List<PointD> Points { get; }
    public int? BadgeNumber { get; }            // badge tool만 set

    public DrawingShape(DrawingTool tool, Rgba color, double lineWidth, PointD start, int? badgeNumber = null)
    {
        Tool = tool; Color = color; LineWidth = lineWidth;
        Points = new List<PointD> { start }; BadgeNumber = badgeNumber;
    }
}

/// <summary>
/// ⌃⌥D 그리기 모드 상태 머신 — 발표/스크린캐스트 annotation. (Swift <c>DrawingState</c> 순수 부분 이식)
///
/// 모디파이어→도구 매핑은 Windows로 옮김: Mac Cmd→Ctrl, Opt→Alt. Win은 무관 모디파이어(→ sticky 도구).
/// onboarding(타이머·영구저장)은 UI 계층 책임 — Core는 상태 전이만. View 본문은 이 클래스를 호출만.
/// </summary>
public sealed class DrawingState
{
    public bool IsDrawingModeActive { get; set; }
    public List<DrawingShape> Shapes { get; } = new();
    public DrawingShape? CurrentShape { get; private set; }

    /// <summary>현재 stroke 두께 — [ ] 키로 조절. startShape 시점에 도형에 캡처.</summary>
    public double LineWidth { get; set; } = Tokens.Drawing.LineWidth;

    /// <summary>번호 뱃지 카운터 — 1부터 자동 증가. ESC/모드 OFF 시 리셋.</summary>
    public int BadgeCounter { get; private set; } = 1;

    /// <summary>현재 눌린 모디파이어 — toolbar preview용.</summary>
    public KeyModifiers CurrentModifiers { get; set; } = KeyModifiers.None;

    /// <summary>Toolbar에서 sticky 선택한 도구. 모디파이어 없을 때 startShape이 사용.</summary>
    public DrawingTool SelectedTool { get; set; } = DrawingTool.Pen;

    public Dictionary<DrawingTool, RectD> ToolbarFrames { get; set; } = new();
    public Dictionary<double, RectD> ThicknessFrames { get; set; } = new();
    public Dictionary<string, RectD> ColorFrames { get; set; } = new();
    public bool IsDraggingToolbar { get; private set; }

    private PointD _dragStartCursor;
    private double _dragStartLeading;
    private double _dragStartBottom;

    /// <summary>지금 드래그 시작하면 그려질 도구. 모디파이어 있으면 그쪽, 없으면 selectedTool.</summary>
    public DrawingTool PreviewTool
        => HasToolMods(CurrentModifiers) ? ToolFor(CurrentModifiers) : SelectedTool;

    /// <summary>
    /// 모디파이어 → 도구. 우선순위 specific → general:
    /// Shift+Alt=badge, Ctrl+Alt=highlighter, Ctrl+Shift=ellipse, Ctrl=rectangle, Alt=arrow, Shift=line, 그 외 pen.
    /// (Mac Cmd→Ctrl, Opt→Alt. Win은 도구에 무관 → pen.)
    /// </summary>
    public static DrawingTool ToolFor(KeyModifiers m)
    {
        bool ctrl = m.HasFlag(KeyModifiers.Control); // was Cmd
        bool shift = m.HasFlag(KeyModifiers.Shift);
        bool alt = m.HasFlag(KeyModifiers.Alt);      // was Opt
        if (shift && alt && !ctrl) return DrawingTool.Badge;
        if (ctrl && alt) return DrawingTool.Highlighter;
        if (ctrl && shift) return DrawingTool.Ellipse;
        if (ctrl) return DrawingTool.Rectangle;
        if (alt) return DrawingTool.Arrow;
        if (shift) return DrawingTool.Line;
        return DrawingTool.Pen;
    }

    // 도구를 결정하는 모디파이어가 있나(Ctrl/Shift/Alt). Win은 제외 → sticky 도구 사용.
    private static bool HasToolMods(KeyModifiers m)
        => m.HasFlag(KeyModifiers.Control) || m.HasFlag(KeyModifiers.Shift) || m.HasFlag(KeyModifiers.Alt);

    /// <summary>Toolbar 도구 영역 hit-test. 적중 시 selectedTool 갱신, true.</summary>
    public bool HitToolbarAndSelect(PointD p)
    {
        foreach (var (tool, rect) in ToolbarFrames)
            if (rect.Contains(p)) { SelectedTool = tool; return true; }
        return false;
    }

    /// <summary>두께 dot hit-test. 적중 시 lineWidth 갱신, true.</summary>
    public bool HitThicknessAndSelect(PointD p)
    {
        foreach (var (width, rect) in ThicknessFrames)
            if (rect.Contains(p)) { LineWidth = width; return true; }
        return false;
    }

    /// <summary>색 dot hit-test. 적중 시 색 이름 반환(caller가 적용), 없으면 null.</summary>
    public string? ColorAt(PointD p)
    {
        foreach (var (name, rect) in ColorFrames)
            if (rect.Contains(p)) return name;
        return null;
    }

    /// <summary>Toolbar drag 시작.</summary>
    public void BeginToolbarDrag(PointD cursor, double leading, double bottom)
    {
        IsDraggingToolbar = true;
        _dragStartCursor = cursor;
        _dragStartLeading = leading;
        _dragStartBottom = bottom;
    }

    /// <summary>드래그 중 새 위치(누적 offset). 드래그 중 아니면 null.</summary>
    public (double Leading, double Bottom)? ToolbarDragDelta(PointD cursor)
    {
        if (!IsDraggingToolbar) return null;
        double dx = cursor.X - _dragStartCursor.X;
        double dy = cursor.Y - _dragStartCursor.Y;
        return (_dragStartLeading + dx, _dragStartBottom + dy);
    }

    public void EndToolbarDrag() => IsDraggingToolbar = false;

    /// <summary>드래그 시작 — badge는 즉시 commit, 그 외는 currentShape 설정.</summary>
    public void StartShape(PointD point, KeyModifiers modifiers, Rgba color)
    {
        DrawingTool chosen = HasToolMods(modifiers) ? ToolFor(modifiers) : SelectedTool;
        if (chosen == DrawingTool.Badge)
        {
            Shapes.Add(new DrawingShape(DrawingTool.Badge, color, LineWidth, point, BadgeCounter));
            BadgeCounter += 1;
            CurrentShape = null;
        }
        else
        {
            CurrentShape = new DrawingShape(chosen, color, LineWidth, point);
        }
    }

    /// <summary>드래그 중 — pen·highlighter는 점 누적, 도형류는 끝점만 갱신, badge는 무시.</summary>
    public void UpdateShape(PointD point)
    {
        var s = CurrentShape;
        if (s is null) return;
        switch (s.Tool)
        {
            case DrawingTool.Pen:
            case DrawingTool.Highlighter:
                s.Points.Add(point);
                break;
            case DrawingTool.Arrow:
            case DrawingTool.Line:
            case DrawingTool.Rectangle:
            case DrawingTool.Ellipse:
                if (s.Points.Count >= 2) s.Points[1] = point;
                else s.Points.Add(point);
                break;
            case DrawingTool.Badge:
                return; // badge는 startShape에서 이미 commit
        }
    }

    /// <summary>드래그 종료 — points >= 2일 때만 commit (단일 점 클릭 폐기).</summary>
    public void EndShape()
    {
        if (CurrentShape is { } s && s.Points.Count >= 2) Shapes.Add(s);
        CurrentShape = null;
    }

    /// <summary>Cmd+Z — 마지막 도형 1개 제거. badge면 counter 1 감소(다음에 재사용).</summary>
    public void UndoLastShape()
    {
        if (Shapes.Count == 0) return;
        var removed = Shapes[^1];
        Shapes.RemoveAt(Shapes.Count - 1);
        if (removed.Tool == DrawingTool.Badge)
            BadgeCounter = Math.Max(1, BadgeCounter - 1);
    }

    /// <summary>[ — 두께 한 단계 감소. 변경 후 두께 반환.</summary>
    public double DecreaseLineWidth()
    {
        var steps = Tokens.Drawing.LineWidthSteps;
        int idx = Array.FindIndex(steps, w => Math.Abs(w - LineWidth) < 0.01);
        if (idx > 0)
        {
            LineWidth = steps[idx - 1];
        }
        else
        {
            // step 사이 값이면 가장 가까운 작은 step (ascending이라 마지막 < 값)
            double? smaller = null;
            foreach (var w in steps) if (w < LineWidth) smaller = w;
            if (smaller is { } v) LineWidth = v;
        }
        return LineWidth;
    }

    /// <summary>] — 두께 한 단계 증가. 변경 후 두께 반환.</summary>
    public double IncreaseLineWidth()
    {
        var steps = Tokens.Drawing.LineWidthSteps;
        int idx = Array.FindIndex(steps, w => Math.Abs(w - LineWidth) < 0.01);
        if (idx >= 0 && idx < steps.Length - 1)
        {
            LineWidth = steps[idx + 1];
        }
        else
        {
            double? larger = null;
            foreach (var w in steps) if (w > LineWidth) { larger = w; break; }
            if (larger is { } v) LineWidth = v;
        }
        return LineWidth;
    }

    /// <summary>ESC — 모든 도형 clear + 모드 종료 + 카운터/두께/도구 리셋. clean slate.</summary>
    public void ClearAndExit()
    {
        Shapes.Clear();
        CurrentShape = null;
        IsDrawingModeActive = false;
        BadgeCounter = 1;
        LineWidth = Tokens.Drawing.LineWidth;
        SelectedTool = DrawingTool.Pen;
    }

    /// <summary>⌃⌥D — 모드만 전환. 도형 유지, 진행 중 stroke만 폐기. (onboarding은 UI 계층)</summary>
    public void ToggleMode()
    {
        IsDrawingModeActive = !IsDrawingModeActive;
        CurrentShape = null;
    }
}

/// <summary>도구 표시 이름(한국어 source). 번역은 localization 계층. (Swift Tool.displayName)</summary>
public static class DrawingToolNames
{
    public static string DisplayName(this DrawingTool tool) => tool switch
    {
        DrawingTool.Pen => "펜",
        DrawingTool.Line => "직선",
        DrawingTool.Arrow => "화살표",
        DrawingTool.Rectangle => "사각형",
        DrawingTool.Ellipse => "타원",
        DrawingTool.Highlighter => "형광펜",
        DrawingTool.Badge => "뱃지",
        _ => tool.ToString(),
    };
}
