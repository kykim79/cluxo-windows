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
/// 맥 PreferencesView 대응: 상단 세그먼트 탭(링/효과/일반) + 섹션 카드 + 색 스와치 + 세그먼트
/// 피커 + 토글 스위치. 코드로 UI 구성(XAML 빌드 의존 회피). <see cref="BuildPanel"/>은 셀프테스트 재사용.
/// </summary>
internal sealed class SettingsWindow : Window
{
    // ── macOS 풍 라이트 팔레트 ──────────────────────────────────
    private static readonly Brush Accent = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    private static readonly Brush WindowBg = Frozen(Color.FromRgb(0xF2, 0xF2, 0xF6));
    private static readonly Brush CardBg = Frozen(Colors.White);
    private static readonly Brush CardBorder = Frozen(Color.FromRgb(0xE3, 0xE3, 0xE9));
    private static readonly Brush TextPrimary = Frozen(Color.FromRgb(0x1D, 0x1D, 0x1F));
    private static readonly Brush TextMuted = Frozen(Color.FromRgb(0x70, 0x70, 0x78));
    private static readonly Brush SegTrack = Frozen(Color.FromRgb(0xE5, 0xE5, 0xEB));
    private static readonly Brush ThumbBg = Frozen(Colors.White);

    private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public SettingsWindow(CursorSettings settings, ILaunchAtLogin launch)
    {
        Title = "Cluxo 설정";
        Width = 460; Height = 660;
        MinWidth = 420; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = WindowBg;
        Content = BuildPanel(settings, launch);
    }

    /// <summary>설정 컨트롤 패널 — Window 콘텐츠 + 셀프테스트 렌더 공용(Window 부모 없이 렌더 가능).</summary>
    internal static FrameworkElement BuildPanel(CursorSettings s, ILaunchAtLogin launch)
    {
        var titles = new[] { "링", "효과", "일반" };
        var scrollers = new FrameworkElement[]
        {
            WrapScroll(RingTab(s)),
            WrapScroll(EffectsTab(s)),
            WrapScroll(GeneralTab(s, launch)),
        };

        var host = new ContentControl { Content = scrollers[0] };
        var bar = Segmented(titles, 0, i => host.Content = scrollers[i], big: true);

        var root = new DockPanel { Background = WindowBg };
        var barWrap = new Border { Padding = new Thickness(16, 14, 16, 8), Child = bar };
        DockPanel.SetDock(barWrap, Dock.Top);
        root.Children.Add(barWrap);
        root.Children.Add(host);
        return root;
    }

    private static ScrollViewer WrapScroll(FrameworkElement content)
    {
        content.Margin = new Thickness(16, 2, 16, 18);
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    // ── 탭 ──────────────────────────────────────────────────────
    private static FrameworkElement RingTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Section("색상", ColorSwatches(s)));
        p.Children.Add(Section("모양", SegEnum(s.RingShape, v => s.RingShape = v, ShortShape)));
        p.Children.Add(Section("크기", SegEnum(s.RingSize, v => s.RingSize = v, ShortSize)));
        p.Children.Add(Section("투명도", SliderRow(s.RingOpacity, 0.2, 1.0, 0.05, v => s.RingOpacity = v, v => $"{(int)System.Math.Round(v * 100)}%")));
        p.Children.Add(Section("테두리",
            Field("두께", SegEnum(s.BorderWeight, v => s.BorderWeight = v, v => v.Label())),
            Field("스타일", SegEnum(s.BorderStyle, v => s.BorderStyle = v, v => v.Label()))));
        p.Children.Add(Section("추가",
            Toggle("이중 링", s.HasInnerRing, v => s.HasInnerRing = v),
            Toggle("링 채우기 (반투명 도넛)", s.IsRingFillEnabled, v => s.IsRingFillEnabled = v),
            Toggle("원근 왜곡", s.IsPerspectiveWarping, v => s.IsPerspectiveWarping = v)));
        return p;
    }

    private static FrameworkElement EffectsTab(CursorSettings s)
    {
        var p = new StackPanel();
        p.Children.Add(Section("애니메이션 속도", SegEnum(s.AnimationSpeed, v => s.AnimationSpeed = v, v => v.Label())));
        p.Children.Add(Section("링 효과",
            Toggle("글로우", s.IsGlowEnabled, v => s.IsGlowEnabled = v),
            Toggle("정지 시 펄스", s.IsIdlePulseEnabled, v => s.IsIdlePulseEnabled = v),
            Toggle("커서 트레일", s.IsTrailEnabled, v => s.IsTrailEnabled = v),
            Toggle("코멧 꼬리", s.IsCometTailEnabled, v => s.IsCometTailEnabled = v)));
        p.Children.Add(Section("흔들어서 찾기",
            Toggle("흔들기 강조", s.IsShakeEnabled, v => s.IsShakeEnabled = v),
            Field("민감도", SegEnum(s.ShakeSensitivity, v => s.ShakeSensitivity = v, v => v.Label()))));
        p.Children.Add(Section("드래그",
            Toggle("기준선", s.IsAnchoredLineEnabled, v => s.IsAnchoredLineEnabled = v),
            Toggle("각도 라벨", s.IsDragAngleLabelEnabled, v => s.IsDragAngleLabelEnabled = v)));
        p.Children.Add(Section("기타",
            Toggle("스크롤 표시", s.IsScrollIndicatorEnabled, v => s.IsScrollIndicatorEnabled = v),
            Toggle("키 입력 표시", s.IsKeystrokeEnabled, v => s.IsKeystrokeEnabled = v),
            Field("키 입력 표시 시간", SliderRow(s.KeystrokeTimeout, 1, 8, 1, v => s.KeystrokeTimeout = v, v => $"{(int)v}초"))));
        return p;
    }

    private static FrameworkElement GeneralTab(CursorSettings s, ILaunchAtLogin launch)
    {
        var p = new StackPanel();
        p.Children.Add(Section("언어", SegEnum(s.PreferredLanguage, v => s.PreferredLanguage = v, v => v.Label())));
        p.Children.Add(Section("시작", Toggle("로그인 시 실행", launch.IsEnabled, v => launch.IsEnabled = v)));
        p.Children.Add(Section("단축키", new TextBlock
        {
            Text = "Ctrl+Alt+D   그리기 모드\n" +
                   "Ctrl+Alt+.   라디얼 메뉴 (가운데 버튼도 가능)\n" +
                   "Ctrl+Alt+I   좌표 표시\n" +
                   "Ctrl+Alt+S / M / K   스포트라이트 / 돋보기 / 키 입력\n" +
                   "Ctrl+Alt+C / H   색 · 모양 순환\n" +
                   "Ctrl+Alt+1~7   색 직접 선택",
            Foreground = TextMuted, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 20,
        }));
        return p;
    }

    // ── 섹션 카드 ───────────────────────────────────────────────
    private static FrameworkElement Section(string label, params FrameworkElement[] children)
    {
        var stack = new StackPanel();
        for (int i = 0; i < children.Length; i++)
        {
            if (i > 0) children[i].Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(children[i]);
        }
        var card = new Border
        {
            Background = CardBg, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), Child = stack,
        };
        var header = new TextBlock
        {
            Text = label, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted, Margin = new Thickness(6, 16, 0, 7),
        };
        var box = new StackPanel();
        box.Children.Add(header);
        box.Children.Add(card);
        return box;
    }

    // 섹션 내부 하위 라벨 + 컨트롤(예: 테두리 두께/스타일).
    private static FrameworkElement Field(string label, FrameworkElement control)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = TextMuted, Margin = new Thickness(2, 0, 0, 5) });
        stack.Children.Add(control);
        return stack;
    }

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
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Frozen(Color.FromRgb(rgba.R, rgba.G, rgba.B)) };
            var ring = new Border
            {
                Width = 38, Height = 38, CornerRadius = new CornerRadius(19), BorderThickness = new Thickness(2.5),
                BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Child = dot,
                Margin = new Thickness(2), Cursor = Cursors.Hand, ToolTip = color.Label(),
            };
            ring.MouseLeftButtonUp += (_, _) => { s.RingColor = color; Apply(color); };
            rings[i] = ring;
            wrap.Children.Add(ring);
        }
        Apply(s.RingColor);
        return wrap;
    }

    // ── 토글 스위치 행 ──────────────────────────────────────────
    private static FrameworkElement Toggle(string label, bool current, Action<bool> onChange)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = TextPrimary, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var sw = Switch(current, onChange);
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(sw, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(sw);
        return grid;
    }

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
        var valueText = new TextBlock { MinWidth = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = TextMuted, FontSize = 12, Text = fmt(current) };
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
