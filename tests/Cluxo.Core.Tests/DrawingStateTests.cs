using Cluxo.Core;
using Xunit;

namespace Cluxo.Core.Tests;

// DrawingState — ⌃⌥D 그리기 모드 상태 머신 검증. (Swift DrawingStateTests 이식)
//
// 모디파이어 매핑(Mac→Windows): Cmd→Ctrl, Opt→Alt, Shift→Shift, Win=무관(Mac Control 자리).
// 도메인 규칙: startShape가 도구 결정 / updateShape 분기(pen 누적, 도형류 끝점) /
// endShape는 points>=2만 commit / clearAndExit 전체 리셋 / toggleMode 도형 유지·stroke 폐기.
public class DrawingStateTests
{
    private static readonly Rgba Red = Rgba.Red;

    // MARK: 모디파이어 → 도구 매핑

    [Fact]
    public void DragWithoutModifiers_PicksPen()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        Assert.Equal(DrawingTool.Pen, s.CurrentShape?.Tool);
    }

    [Fact]
    public void ShiftDrag_PicksLine()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Shift, Red);
        Assert.Equal(DrawingTool.Line, s.CurrentShape?.Tool);
    }

    [Fact]
    public void OptionDrag_PicksArrow()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Alt, Red);
        Assert.Equal(DrawingTool.Arrow, s.CurrentShape?.Tool);
    }

    [Fact]
    public void OptionPlusShift_PicksBadge()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Alt | KeyModifiers.Shift, Red);
        Assert.Null(s.CurrentShape); // badge는 즉시 commit
        Assert.Equal(DrawingTool.Badge, s.Shapes[0].Tool);
    }

    [Fact]
    public void UnrelatedModifierOnly_StillPicksPen()
    {
        // Mac control-only → pen. Windows: Win은 도구에 무관 → sticky(default pen).
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Win, Red);
        Assert.Equal(DrawingTool.Pen, s.CurrentShape?.Tool);
    }

    // MARK: updateShape 분기

    [Fact]
    public void UpdatePen_AppendsAllPoints()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(0, 0), KeyModifiers.None, Red);
        s.UpdateShape(new PointD(10, 10));
        s.UpdateShape(new PointD(20, 20));
        s.UpdateShape(new PointD(30, 30));
        Assert.Equal(4, s.CurrentShape?.Points.Count);
        Assert.Equal(new PointD(30, 30), s.CurrentShape!.Points[^1]);
    }

    [Fact]
    public void UpdateLine_ReplacesEndpointOnly()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(0, 0), KeyModifiers.Shift, Red);
        s.UpdateShape(new PointD(10, 10));
        s.UpdateShape(new PointD(20, 20));
        s.UpdateShape(new PointD(30, 30));
        Assert.Equal(2, s.CurrentShape?.Points.Count);
        Assert.Equal(new PointD(0, 0), s.CurrentShape!.Points[0]);
        Assert.Equal(new PointD(30, 30), s.CurrentShape!.Points[1]);
    }

    [Fact]
    public void UpdateArrow_ReplacesEndpointOnly()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(5, 5), KeyModifiers.Alt, Red);
        s.UpdateShape(new PointD(100, 50));
        s.UpdateShape(new PointD(200, 100));
        Assert.Equal(2, s.CurrentShape?.Points.Count);
        Assert.Equal(new PointD(5, 5), s.CurrentShape!.Points[0]);
        Assert.Equal(new PointD(200, 100), s.CurrentShape!.Points[1]);
    }

    // MARK: endShape 가드

    [Fact]
    public void EndShape_SinglePoint_Discarded()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        Assert.Equal(1, s.CurrentShape?.Points.Count);
        s.EndShape();
        Assert.Empty(s.Shapes);
        Assert.Null(s.CurrentShape);
    }

    [Fact]
    public void EndShape_TwoPoints_Committed()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        s.UpdateShape(new PointD(1, 1));
        s.EndShape();
        Assert.Single(s.Shapes);
        Assert.Null(s.CurrentShape);
    }

    [Fact]
    public void EndShape_WithoutStart_Noop()
    {
        var s = new DrawingState();
        s.EndShape();
        Assert.Empty(s.Shapes);
        Assert.Null(s.CurrentShape);
    }

    // MARK: clearAndExit / toggleMode

    [Fact]
    public void ClearAndExit_RemovesAllAndDisablesMode()
    {
        var s = new DrawingState { IsDrawingModeActive = true };
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        s.UpdateShape(new PointD(1, 1));
        s.EndShape();
        s.StartShape(PointD.Zero, KeyModifiers.None, Red); // 진행 중 도형도
        s.ClearAndExit();
        Assert.Empty(s.Shapes);
        Assert.Null(s.CurrentShape);
        Assert.False(s.IsDrawingModeActive);
    }

    [Fact]
    public void ToggleMode_KeepsShapes_DropsCurrentStroke()
    {
        var s = new DrawingState { IsDrawingModeActive = true };
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        s.UpdateShape(new PointD(1, 1));
        s.EndShape(); // 1 committed
        s.StartShape(PointD.Zero, KeyModifiers.None, Red); // 진행 중 stroke
        s.ToggleMode();
        Assert.False(s.IsDrawingModeActive);
        Assert.Single(s.Shapes);   // 완성 도형 유지
        Assert.Null(s.CurrentShape); // 진행 중만 폐기
    }

    [Fact]
    public void ToggleMode_Twice_ReturnsToActive()
    {
        var s = new DrawingState();
        s.ToggleMode();
        Assert.True(s.IsDrawingModeActive);
        s.ToggleMode();
        Assert.False(s.IsDrawingModeActive);
    }

    // MARK: 신규 도구 (사각형/타원/형광펜/뱃지)

    [Fact]
    public void CmdDrag_PicksRectangle()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Control, Red);
        Assert.Equal(DrawingTool.Rectangle, s.CurrentShape?.Tool);
    }

    [Fact]
    public void CmdShiftDrag_PicksEllipse()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Control | KeyModifiers.Shift, Red);
        Assert.Equal(DrawingTool.Ellipse, s.CurrentShape?.Tool);
    }

    [Fact]
    public void CmdOptDrag_PicksHighlighter()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Control | KeyModifiers.Alt, Red);
        Assert.Equal(DrawingTool.Highlighter, s.CurrentShape?.Tool);
    }

    [Fact]
    public void Highlighter_AccumulatesPointsLikePen()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(0, 0), KeyModifiers.Control | KeyModifiers.Alt, Red);
        s.UpdateShape(new PointD(10, 10));
        s.UpdateShape(new PointD(20, 20));
        Assert.Equal(3, s.CurrentShape?.Points.Count);
    }

    [Fact]
    public void Rectangle_ReplacesEndpointOnly()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(0, 0), KeyModifiers.Control, Red);
        s.UpdateShape(new PointD(10, 10));
        s.UpdateShape(new PointD(100, 50));
        Assert.Equal(2, s.CurrentShape?.Points.Count);
        Assert.Equal(new PointD(100, 50), s.CurrentShape!.Points[1]);
    }

    // MARK: 번호 뱃지

    [Fact]
    public void ShiftOptClick_ImmediatelyCommitsBadge()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(50, 50), KeyModifiers.Shift | KeyModifiers.Alt, Red);
        Assert.Single(s.Shapes);
        Assert.Equal(DrawingTool.Badge, s.Shapes[0].Tool);
        Assert.Equal(1, s.Shapes[0].BadgeNumber);
        Assert.Null(s.CurrentShape);
    }

    [Fact]
    public void Badge_CounterIncrements()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red);
        s.StartShape(new PointD(10, 10), KeyModifiers.Shift | KeyModifiers.Alt, Red);
        s.StartShape(new PointD(20, 20), KeyModifiers.Shift | KeyModifiers.Alt, Red);
        Assert.Equal(new int?[] { 1, 2, 3 }, s.Shapes.Select(x => x.BadgeNumber));
        Assert.Equal(4, s.BadgeCounter);
    }

    [Fact]
    public void Badge_UpdateShape_NoEffect()
    {
        var s = new DrawingState();
        s.StartShape(new PointD(100, 100), KeyModifiers.Shift | KeyModifiers.Alt, Red);
        s.UpdateShape(new PointD(200, 200)); // badge는 update 무시
        Assert.Equal(new[] { new PointD(100, 100) }, s.Shapes[0].Points);
    }

    // MARK: Undo

    [Fact]
    public void UndoLastShape_RemovesOne()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        s.UpdateShape(new PointD(10, 10));
        s.EndShape(); // pen
        s.StartShape(PointD.Zero, KeyModifiers.Shift, Red);
        s.UpdateShape(new PointD(20, 20));
        s.EndShape(); // line
        Assert.Equal(2, s.Shapes.Count);
        s.UndoLastShape();
        Assert.Single(s.Shapes);
        Assert.Equal(DrawingTool.Pen, s.Shapes[0].Tool);
    }

    [Fact]
    public void UndoLastShape_Empty_Noop()
    {
        var s = new DrawingState();
        s.UndoLastShape();
        Assert.Empty(s.Shapes);
    }

    [Fact]
    public void UndoBadge_DecrementsCounter()
    {
        var s = new DrawingState();
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red); // 1
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red); // 2
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red); // 3
        Assert.Equal(4, s.BadgeCounter);
        s.UndoLastShape();
        Assert.Equal(3, s.BadgeCounter);
        Assert.Equal(2, s.Shapes.Count);
    }

    // MARK: 두께 조절

    [Fact]
    public void IncreaseLineWidth_MovesUpByStep()
    {
        var s = new DrawingState();
        Assert.Equal(4, s.LineWidth);
        Assert.Equal(6, s.IncreaseLineWidth());
        Assert.Equal(6, s.LineWidth);
    }

    [Fact]
    public void DecreaseLineWidth_MovesDownByStep()
    {
        var s = new DrawingState();
        Assert.Equal(4, s.LineWidth);
        Assert.Equal(2, s.DecreaseLineWidth());
        Assert.Equal(2, s.LineWidth);
    }

    [Fact]
    public void DecreaseLineWidth_ClampsAtMin()
    {
        var s = new DrawingState { LineWidth = 2 };
        Assert.Equal(2, s.DecreaseLineWidth());
    }

    [Fact]
    public void IncreaseLineWidth_ClampsAtMax()
    {
        var s = new DrawingState { LineWidth = 14 };
        Assert.Equal(14, s.IncreaseLineWidth());
    }

    [Fact]
    public void Shape_CapturesLineWidthAtStart()
    {
        var s = new DrawingState { LineWidth = 10 };
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        s.UpdateShape(new PointD(5, 5));
        s.EndShape();
        s.LineWidth = 14; // 이미 그린 도형 영향 X
        Assert.Equal(10, s.Shapes[0].LineWidth);
    }

    [Fact]
    public void ClearAndExit_ResetsCounterAndWidth()
    {
        var s = new DrawingState { IsDrawingModeActive = true, LineWidth = 14 };
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red);
        s.StartShape(PointD.Zero, KeyModifiers.Shift | KeyModifiers.Alt, Red);
        Assert.Equal(3, s.BadgeCounter);
        s.ClearAndExit();
        Assert.Equal(1, s.BadgeCounter);
        Assert.Equal(Tokens.Drawing.LineWidth, s.LineWidth);
        Assert.Empty(s.Shapes);
        Assert.False(s.IsDrawingModeActive);
    }

    // MARK: selectedTool (toolbar sticky)

    [Fact]
    public void SelectedTool_Default_Pen()
        => Assert.Equal(DrawingTool.Pen, new DrawingState().SelectedTool);

    [Fact]
    public void DragWithoutMods_UsesSelectedTool()
    {
        var s = new DrawingState { SelectedTool = DrawingTool.Rectangle };
        s.StartShape(PointD.Zero, KeyModifiers.None, Red);
        Assert.Equal(DrawingTool.Rectangle, s.CurrentShape?.Tool);
    }

    [Fact]
    public void ModifierOverridesSelectedTool()
    {
        var s = new DrawingState { SelectedTool = DrawingTool.Rectangle };
        s.StartShape(PointD.Zero, KeyModifiers.Shift, Red);
        Assert.Equal(DrawingTool.Line, s.CurrentShape?.Tool); // Shift > sticky
    }

    [Fact]
    public void PreviewTool_NoMods_ReturnsSelected()
    {
        var s = new DrawingState { SelectedTool = DrawingTool.Ellipse, CurrentModifiers = KeyModifiers.None };
        Assert.Equal(DrawingTool.Ellipse, s.PreviewTool);
    }

    [Fact]
    public void PreviewTool_WithMods_ReturnsModifierTool()
    {
        var s = new DrawingState { SelectedTool = DrawingTool.Ellipse, CurrentModifiers = KeyModifiers.Alt };
        Assert.Equal(DrawingTool.Arrow, s.PreviewTool); // 모디파이어 우선
    }

    [Fact]
    public void ClearAndExit_ResetsSelectedTool()
    {
        var s = new DrawingState { SelectedTool = DrawingTool.Arrow };
        s.ClearAndExit();
        Assert.Equal(DrawingTool.Pen, s.SelectedTool);
    }

    // MARK: toolbar hit-test

    [Fact]
    public void HitToolbarAndSelect_Inside_ChangesTool()
    {
        var s = new DrawingState
        {
            ToolbarFrames = new() { [DrawingTool.Rectangle] = new RectD(100, 200, 50, 50) }
        };
        Assert.True(s.HitToolbarAndSelect(new PointD(120, 220)));
        Assert.Equal(DrawingTool.Rectangle, s.SelectedTool);
    }

    [Fact]
    public void HitToolbarAndSelect_Outside_NoChange()
    {
        var s = new DrawingState
        {
            ToolbarFrames = new() { [DrawingTool.Rectangle] = new RectD(100, 200, 50, 50) }
        };
        Assert.False(s.HitToolbarAndSelect(new PointD(300, 300)));
        Assert.Equal(DrawingTool.Pen, s.SelectedTool);
    }

    [Fact]
    public void HitToolbarAndSelect_Badge_SelectsBadge()
    {
        var s = new DrawingState
        {
            ToolbarFrames = new() { [DrawingTool.Badge] = new RectD(100, 200, 50, 50) }
        };
        s.HitToolbarAndSelect(new PointD(120, 220));
        Assert.Equal(DrawingTool.Badge, s.SelectedTool);
        s.StartShape(new PointD(500, 500), KeyModifiers.None, Red); // sticky badge → 즉시 commit
        Assert.Equal(DrawingTool.Badge, s.Shapes[0].Tool);
        Assert.Equal(1, s.Shapes[0].BadgeNumber);
    }

    // MARK: 두께/색 toolbar hit-test

    [Fact]
    public void HitThicknessAndSelect_Inside_ChangesLineWidth()
    {
        var s = new DrawingState { ThicknessFrames = new() { [10.0] = new RectD(50, 50, 24, 24) } };
        Assert.True(s.HitThicknessAndSelect(new PointD(60, 60)));
        Assert.Equal(10, s.LineWidth);
    }

    [Fact]
    public void HitThicknessAndSelect_Outside_NoChange()
    {
        var s = new DrawingState { ThicknessFrames = new() { [10.0] = new RectD(50, 50, 24, 24) } };
        Assert.False(s.HitThicknessAndSelect(new PointD(200, 200)));
        Assert.Equal(Tokens.Drawing.LineWidth, s.LineWidth);
    }

    [Fact]
    public void ColorAt_Inside_ReturnsName()
    {
        var s = new DrawingState { ColorFrames = new() { ["red"] = new RectD(100, 100, 22, 22) } };
        Assert.Equal("red", s.ColorAt(new PointD(110, 110)));
    }

    [Fact]
    public void ColorAt_Outside_ReturnsNull()
    {
        var s = new DrawingState { ColorFrames = new() { ["red"] = new RectD(100, 100, 22, 22) } };
        Assert.Null(s.ColorAt(new PointD(300, 300)));
    }

    // MARK: Toolbar 위치 드래그

    [Fact]
    public void BeginToolbarDrag_SetsDraggingFlag()
    {
        var s = new DrawingState();
        Assert.False(s.IsDraggingToolbar);
        s.BeginToolbarDrag(new PointD(100, 200), leading: 28, bottom: 110);
        Assert.True(s.IsDraggingToolbar);
    }

    [Fact]
    public void ToolbarDragDelta_ReturnsCumulativeOffset()
    {
        var s = new DrawingState();
        s.BeginToolbarDrag(new PointD(100, 200), leading: 28, bottom: 110);
        var delta = s.ToolbarDragDelta(new PointD(150, 240));
        Assert.NotNull(delta);
        Assert.Equal(78, delta!.Value.Leading);  // 28 + (150-100)
        Assert.Equal(150, delta.Value.Bottom);    // 110 + (240-200)
    }

    [Fact]
    public void ToolbarDragDelta_ReturnsNull_WhenNotDragging()
        => Assert.Null(new DrawingState().ToolbarDragDelta(new PointD(100, 100)));

    [Fact]
    public void EndToolbarDrag_ClearsDraggingFlag()
    {
        var s = new DrawingState();
        s.BeginToolbarDrag(PointD.Zero, 0, 0);
        s.EndToolbarDrag();
        Assert.False(s.IsDraggingToolbar);
        Assert.Null(s.ToolbarDragDelta(new PointD(50, 50)));
    }
}
