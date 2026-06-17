using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Ui;

/// <summary>
/// 설정창 — 코디네이터의 라이브 <see cref="CursorSettings"/>를 직접 편집한다.
/// 변경 시 CursorSettings.Changed → 코디네이터가 즉시 적용 + ISettingsStore가 디바운스 영구화.
///
/// 맥 시스템 설정 풍: 상단 세그먼트 탭(링/효과/모드/일반) + 라벨 좌측·컨트롤 우측의 그룹 카드.
/// 탭별로 창 높이를 콘텐츠에 맞춰(SizeToContent) 스크롤 없이 표시. 코드로 UI 구성(XAML 의존 회피).
/// <see cref="BuildPanel"/>은 셀프테스트 렌더에도 재사용.
/// </summary>
internal sealed class SettingsWindow : Window
{
    // ── macOS 풍 라이트 팔레트 ──────────────────────────────────
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    private static readonly Brush WindowBg = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF6));
    private static readonly Brush CardBg = Frozen(Colors.White);
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0xE3, 0xE3, 0xE9));
    private static readonly Brush DividerBrush = Frozen(Color.FromRgb(0xEC, 0xEC, 0xF1));
    private static readonly Brush NoteBg = Frozen(Color.FromRgb(0xEC, 0xEC, 0xF2));
    private static readonly Brush TextPrimary = Frozen(Color.FromRgb(0x1D, 0x1D, 0x1F));
    private static readonly Brush TextMuted = Frozen(Color.FromRgb(0x70, 0x70, 0x78));
    private static readonly Brush SegTrack = Frozen(Color.FromRgb(0xE5, 0xE5, 0xEB));
    private static readonly Brush ThumbBg = Frozen(Colors.White);

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public SettingsWindow(CursorSettings settings, ILaunchAtLogin launch)
    {
        Title = "Cluxo 설정";
        Width = 430;
        SizeToContent = SizeToContent.Height; // 탭별 콘텐츠 높이에 맞춰 — 스크롤 없이
        MaxHeight = 900;
        MinWidth = 430; MaxWidth = 430;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = WindowBg;
        Content = BuildPanel(settings, launch);
    }

    /// <summary>설정 컨트롤 패널 — Window 콘텐츠 + 셀프테스트 렌더 공용(Window 부모 없이 렌더 가능).</summary>
    internal static FrameworkElement BuildPanel(CursorSettings s, ILaunchAtLogin launch)
    {
        var titles = new[] { "링", "효과", "모드", "단축키", "일반" };
        var tabs = new FrameworkElement[]
        {
            RingTab(s), EffectsTab(s), ModesTab(s), ShortcutsTab(s), GeneralTab(s, launch),
        };
        foreach (var t in tabs) t.Margin = new Thickness(16, 6, 16, 16);

        var host = new ContentControl { Content = tabs[0] };
        var bar = Segmented(titles, 0, i => host.Content = tabs[i], big: true);

        var root = new StackPanel { Background = WindowBg };
        root.Children.Add(new Border { Padding = new Thickness(16, 14, 16, 6), Child = bar });
        root.Children.Add(host);
        return root;
    }

    // ── 탭 ──────────────────────────────────────────────────────
    private static FrameworkElement RingTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Card(
            ("색상", LeftAlign(ColorSwatches(s))),
            ("모양", SegEnum(s.RingShape, v => s.RingShape = v, ShortShape)),
            ("크기", SegEnum(s.RingSize, v => s.RingSize = v, ShortSize)),
            ("투명도", SliderRow(s.RingOpacity, 0.2, 1.0, 0.05, v => s.RingOpacity = v, v => $"{(int)System.Math.Round(v * 100)}%"))));
        p.Children.Add(Card(
            ("테두리 두께", SegEnum(s.BorderWeight, v => s.BorderWeight = v, v => v.Label())),
            ("선 스타일", SegEnum(s.BorderStyle, v => s.BorderStyle = v, v => v.Label()))));
        p.Children.Add(Card(
            ("이중 링", Switch(s.HasInnerRing, v => s.HasInnerRing = v)),
            ("링 채우기", Switch(s.IsRingFillEnabled, v => s.IsRingFillEnabled = v)),
            ("원근 왜곡", Switch(s.IsPerspectiveWarping, v => s.IsPerspectiveWarping = v))));
        return p;
    }

    private static FrameworkElement EffectsTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Card(("애니메이션 속도", SegEnum(s.AnimationSpeed, v => s.AnimationSpeed = v, v => v.Label()))));
        p.Children.Add(Card(
            ("글로우", Switch(s.IsGlowEnabled, v => s.IsGlowEnabled = v)),
            ("정지 펄스", Switch(s.IsIdlePulseEnabled, v => s.IsIdlePulseEnabled = v)),
            ("커서 트레일", Switch(s.IsTrailEnabled, v => s.IsTrailEnabled = v)),
            ("코멧 꼬리", Switch(s.IsCometTailEnabled, v => s.IsCometTailEnabled = v))));
        p.Children.Add(Card(
            ("흔들기로 찾기", Switch(s.IsShakeEnabled, v => s.IsShakeEnabled = v)),
            ("민감도", SegEnum(s.ShakeSensitivity, v => s.ShakeSensitivity = v, v => v.Label()))));
        p.Children.Add(Card(
            ("드래그 기준선", Switch(s.IsAnchoredLineEnabled, v => s.IsAnchoredLineEnabled = v)),
            ("드래그 각도", Switch(s.IsDragAngleLabelEnabled, v => s.IsDragAngleLabelEnabled = v))));
        p.Children.Add(Card(
            ("스크롤 표시", Switch(s.IsScrollIndicatorEnabled, v => s.IsScrollIndicatorEnabled = v)),
            ("키 입력 표시", Switch(s.IsKeystrokeEnabled, v => s.IsKeystrokeEnabled = v)),
            ("표시 시간", SliderRow(s.KeystrokeTimeout, 1, 8, 1, v => s.KeystrokeTimeout = v, v => $"{(int)v}초"))));
        return p;
    }

    private static FrameworkElement ModesTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Note("값은 저장됩니다. 화면 렌더(스포트라이트 디밍·돋보기 확대)는 준비 중 — 토글은 라디얼/단축키(⌃⌥S·⌃⌥M)."));
        p.Children.Add(Card(
            ("반경", SliderRow(s.SpotlightRadius, 60, 250, 10, v => s.SpotlightRadius = v, v => $"{(int)v}pt")),
            ("경계", SliderRow(s.SpotlightEdgeSoftness, 0, 1, 0.05, v => s.SpotlightEdgeSoftness = v, v => $"{(int)System.Math.Round(v * 100)}%"))));
        p.Children.Add(Card(
            ("돋보기 배율", SliderRow(s.MagnifierZoom, 1.5, 4.0, 0.5, v => s.MagnifierZoom = v, v => $"{v:0.0}×")),
            ("렌즈 크기", SliderRow(s.MagnifierSize, 120, 300, 20, v => s.MagnifierSize = v, v => $"{(int)v}pt"))));
        return p;
    }

    private static FrameworkElement ShortcutsTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Card(
            ("그리기 모드", KeyRecorder(s.HotkeyDrawing, v => s.HotkeyDrawing = v)),
            ("좌표 표시", KeyRecorder(s.HotkeyInspector, v => s.HotkeyInspector = v)),
            ("스포트라이트", KeyRecorder(s.HotkeySpotlight, v => s.HotkeySpotlight = v)),
            ("돋보기", KeyRecorder(s.HotkeyMagnifier, v => s.HotkeyMagnifier = v)),
            ("키 입력 표시", KeyRecorder(s.HotkeyKeystroke, v => s.HotkeyKeystroke = v))));
        p.Children.Add(Note("모든 단축키는 Ctrl+Alt 조합. 항목을 누르고 새 키를 누르면 바뀝니다 (ESC 취소)."));
        p.Children.Add(Note(
            "고정 단축키\n" +
            "Ctrl+Alt+.  라디얼 메뉴 (가운데 버튼도 가능)\n" +
            "Ctrl+Alt+C / H  색 · 모양 순환\n" +
            "Ctrl+Alt+1~7  색 직접 선택"));
        return p;
    }

    private static FrameworkElement GeneralTab(CursorSettings s, ILaunchAtLogin launch)
    {
        var p = new StackPanel();
        p.Children.Add(Card(
            ("언어", SegEnum(s.PreferredLanguage, v => s.PreferredLanguage = v, v => v.Label())),
            ("로그인 시 실행", Switch(launch.IsEnabled, v => launch.IsEnabled = v))));
        return p;
    }

    // ── 키 레코더 — 클릭 후 키를 누르면 그 키로 재지정(모디파이어 Ctrl+Alt 고정) ──
    private static FrameworkElement KeyRecorder(string current, Action<string> onChange)
    {
        string cur = current;
        bool capturing = false;
        var txt = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary };
        var box = new Border
        {
            Child = txt, Background = SegTrack, CornerRadius = new CornerRadius(7), Padding = new Thickness(12, 5, 12, 5),
            Cursor = Cursors.Hand, Focusable = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 78,
        };
        void Render() { txt.Text = capturing ? "키 입력…" : "Ctrl+Alt+" + KeyDisplay(cur); box.Background = capturing ? Accent : SegTrack; txt.Foreground = capturing ? ThumbBg : TextPrimary; }
        Render();
        box.MouseLeftButtonUp += (_, _) => { capturing = true; Render(); Keyboard.Focus(box); };
        box.LostKeyboardFocus += (_, _) => { if (capturing) { capturing = false; Render(); } };
        box.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifier(key)) return;
            if (key == Key.Escape) { capturing = false; Render(); return; }
            if (KeyToName(key) is { } name) { cur = name; onChange(name); capturing = false; Render(); }
        };
        return box;
    }

    private static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static string? KeyToName(Key k)
    {
        if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
        if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
        return k switch { Key.OemPeriod => "Period", Key.OemComma => "Comma", _ => null };
    }

    private static string KeyDisplay(string name) => name switch { "Period" => ".", "Comma" => ",", _ => name };

    // ── 그룹 카드 + 라벨 좌측 행 ────────────────────────────────
    private static FrameworkElement Card(params (string Label, FrameworkElement Control)[] rows)
    {
        var stack = new StackPanel();
        for (int i = 0; i < rows.Length; i++)
        {
            if (i > 0) stack.Children.Add(new Border { Height = 1, Background = DividerBrush, Margin = new Thickness(-14, 0, -14, 0) });
            stack.Children.Add(Row(rows[i].Label, rows[i].Control));
        }
        return new Border
        {
            Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 3, 14, 3),
            Child = stack, Margin = new Thickness(0, 0, 0, 12),
        };
    }

    // 라벨(좌) + 컨트롤(우). 맥 시스템 설정 행 스타일 — 세로 공간 절약.
    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = TextPrimary, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }

    private static FrameworkElement Note(string text) => new Border
    {
        Background = NoteBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 12),
        Child = new TextBlock { Text = text, Foreground = TextMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 19 },
    };

    private static FrameworkElement LeftAlign(FrameworkElement e) { e.HorizontalAlignment = HorizontalAlignment.Left; return e; }

    // ── 세그먼트 컨트롤 (탭바 + enum 피커 공용) ─────────────────
    private static FrameworkElement Segmented(string[] labels, int selected, Action<int> onSelect, bool big = false)
    {
        var grid = new UniformGrid { Rows = 1, Columns = labels.Length };
        var track = new Border
        {
            CornerRadius = new CornerRadius(big ? 9 : 7), Background = SegTrack,
            Padding = new Thickness(2), Height = big ? 34 : 28, Child = grid, SnapsToDevicePixels = true,
        };
        var cells = new Border[labels.Length];
        var texts = new TextBlock[labels.Length];
        int sel = selected < 0 ? 0 : selected;

        void Apply()
        {
            for (int i = 0; i < labels.Length; i++)
            {
                bool on = i == sel;
                cells[i].Background = on ? ThumbBg : Brushes.Transparent;
                cells[i].Effect = on ? new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.16, Color = Colors.Black } : null;
                texts[i].Foreground = on ? TextPrimary : TextMuted;
                texts[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var t = new TextBlock
            {
                Text = labels[i], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                FontSize = big ? 13 : 12, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            texts[i] = t;
            var cell = new Border { CornerRadius = new CornerRadius(big ? 7 : 6), Margin = new Thickness(1), Child = t, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            cell.MouseLeftButtonUp += (_, _) => { sel = idx; Apply(); onSelect(idx); };
            cells[i] = cell;
            grid.Children.Add(cell);
        }
        Apply();
        return track;
    }

    private static FrameworkElement SegEnum<T>(T current, Action<T> onChange, Func<T, string> labelOf) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return Segmented(values.Select(labelOf).ToArray(), Array.IndexOf(values, current), i => onChange(values[i]));
    }

    // ── 색 스와치 ───────────────────────────────────────────────
    private static FrameworkElement ColorSwatches(CursorSettings s)
    {
        var wrap = new WrapPanel();
        var colors = Enum.GetValues<RingColor>().Where(c => c != RingColor.Custom).ToArray();
        var rings = new Border[colors.Length];

        void Apply(RingColor sel)
        {
            for (int i = 0; i < colors.Length; i++)
                rings[i].BorderBrush = colors[i] == sel ? Accent : Brushes.Transparent;
        }

        for (int i = 0; i < colors.Length; i++)
        {
            var color = colors[i];
            var rgba = color.Color();
            var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Frozen(Color.FromRgb(rgba.R, rgba.G, rgba.B)) };
            var ring = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(17), BorderThickness = new Thickness(2.5),
                BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = dot,
                Margin = new Thickness(1), Cursor = Cursors.Hand, ToolTip = color.Label(),
            };
            ring.MouseLeftButtonUp += (_, _) => { s.RingColor = color; Apply(color); };
            rings[i] = ring;
            wrap.Children.Add(ring);
        }
        Apply(s.RingColor);
        return wrap;
    }

    // ── 토글 스위치 ─────────────────────────────────────────────
    private static FrameworkElement Switch(bool current, Action<bool> onChange)
    {
        bool on = current;
        var thumb = new Border
        {
            Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = ThumbBg,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2),
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 0.5, Opacity = 0.3, Color = Colors.Black },
        };
        var track = new Border
        {
            Width = 40, Height = 24, CornerRadius = new CornerRadius(12),
            Background = on ? Accent : SegTrack, Child = thumb, Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        track.MouseLeftButtonUp += (_, _) =>
        {
            on = !on;
            track.Background = on ? Accent : SegTrack;
            thumb.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            onChange(on);
        };
        return track;
    }

    // ── 슬라이더 행 (슬라이더 + 우측 값) ────────────────────────
    private static FrameworkElement SliderRow(double current, double min, double max, double tick, Action<double> onChange, Func<double, string> fmt)
    {
        var valueText = new TextBlock { MinWidth = 42, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = TextMuted, FontSize = 12, Text = fmt(current) };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = current, TickFrequency = tick, IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        slider.ValueChanged += (_, e) => { valueText.Text = fmt(e.NewValue); onChange(e.NewValue); };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);
        return grid;
    }

    private static string ShortShape(RingShape sh) => sh switch
    {
        RingShape.Circle => "원형", RingShape.Squircle => "사각형",
        RingShape.Rhombus => "마름모", RingShape.Hexagon => "육각형", _ => sh.ToString(),
    };

    private static string ShortSize(RingSize z) => z switch
    {
        RingSize.Small => "작게", RingSize.Medium => "보통",
        RingSize.Large => "크게", RingSize.XLarge => "매우 크게", _ => z.ToString(),
    };
}
