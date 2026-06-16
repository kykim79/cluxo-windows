using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cluxo.Core;
using Cluxo.Core.Platform;

namespace Cluxo.Windows.App.Ui;

/// <summary>
/// 설정창 — 코디네이터의 라이브 <see cref="CursorSettings"/>를 직접 편집한다.
/// 변경 시 CursorSettings.Changed → 코디네이터가 즉시 적용 + ISettingsStore가 디바운스 영구화.
///
/// 노출 범위: 현재 코디네이터가 라이브 반영하는 설정(링 색·크기·투명도·애니메이션 속도·흔들기 민감도·
/// 키 입력 시간) + 영구화 설정(링 모양·언어·로그인 시 실행). 효과 토글 등은 렌더·코디네이터 배선
/// 확장 시 추가. 코드로 UI 구성(XAML 빌드 의존 회피). <see cref="BuildPanel"/>은 셀프테스트 렌더에도 재사용.
/// </summary>
internal sealed class SettingsWindow : Window
{
    public SettingsWindow(CursorSettings settings, ILaunchAtLogin launch)
    {
        Title = "Cluxo 설정";
        Width = 440;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanMinimize;
        ShowInTaskbar = true;
        Content = new ScrollViewer
        {
            Content = BuildPanel(settings, launch),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    /// <summary>설정 컨트롤 패널 — Window 콘텐츠 + 셀프테스트 렌더 공용(Window 부모 없이 렌더 가능).</summary>
    internal static FrameworkElement BuildPanel(CursorSettings s, ILaunchAtLogin launch)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };

        panel.Children.Add(Header("링", first: true));
        panel.Children.Add(EnumRow("색", s.RingColor, v => s.RingColor = v, v => v.Label()));
        panel.Children.Add(EnumRow("모양", s.RingShape, v => s.RingShape = v, v => v.Label()));
        panel.Children.Add(EnumRow("크기", s.RingSize, v => s.RingSize = v, v => v.Label()));
        panel.Children.Add(SliderRow("투명도", s.RingOpacity, 0.2, 1.0, 0.05, v => s.RingOpacity = v, v => $"{(int)Math.Round(v * 100)}%"));

        panel.Children.Add(Header("효과"));
        panel.Children.Add(CheckRow("트레일", s.IsTrailEnabled, v => s.IsTrailEnabled = v));
        panel.Children.Add(CheckRow("코멧 꼬리", s.IsCometTailEnabled, v => s.IsCometTailEnabled = v));
        panel.Children.Add(CheckRow("흔들기로 찾기", s.IsShakeEnabled, v => s.IsShakeEnabled = v));
        panel.Children.Add(CheckRow("스크롤 표시", s.IsScrollIndicatorEnabled, v => s.IsScrollIndicatorEnabled = v));
        panel.Children.Add(CheckRow("드래그 기준선", s.IsAnchoredLineEnabled, v => s.IsAnchoredLineEnabled = v));
        panel.Children.Add(CheckRow("키 입력 표시", s.IsKeystrokeEnabled, v => s.IsKeystrokeEnabled = v));

        panel.Children.Add(Header("동작"));
        panel.Children.Add(EnumRow("애니메이션 속도", s.AnimationSpeed, v => s.AnimationSpeed = v, v => v.Label()));
        panel.Children.Add(EnumRow("흔들기 민감도", s.ShakeSensitivity, v => s.ShakeSensitivity = v, v => v.Label()));
        panel.Children.Add(SliderRow("키 입력 표시 시간", s.KeystrokeTimeout, 1, 8, 1, v => s.KeystrokeTimeout = v, v => $"{(int)v}초"));

        panel.Children.Add(Header("일반"));
        panel.Children.Add(EnumRow("언어", s.PreferredLanguage, v => s.PreferredLanguage = v, v => v.Label()));
        panel.Children.Add(CheckRow("로그인 시 실행", launch.IsEnabled, v => launch.IsEnabled = v));

        return panel;
    }

    private static TextBlock Header(string text, bool first = false) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0xCC)),
        Margin = new Thickness(0, first ? 0 : 16, 0, 6),
    };

    private static FrameworkElement EnumRow<T>(string label, T current, Action<T> onChange, Func<T, string> labelOf)
        where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var combo = new ComboBox { Width = 200, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var v in values) combo.Items.Add(new ComboBoxItem { Content = labelOf(v), Tag = v });
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(values, current));
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem { Tag: T t }) onChange(t);
        };
        return Row(label, combo);
    }

    private static FrameworkElement SliderRow(string label, double current, double min, double max, double tick,
        Action<double> onChange, Func<double, string> fmt)
    {
        var valueText = new TextBlock { Width = 48, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = current, Width = 150,
            TickFrequency = tick, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        valueText.Text = fmt(current);
        slider.ValueChanged += (_, e) => { valueText.Text = fmt(e.NewValue); onChange(e.NewValue); };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(slider);
        sp.Children.Add(valueText);
        return Row(label, sp);
    }

    private static FrameworkElement CheckRow(string label, bool current, Action<bool> onChange)
    {
        var chk = new CheckBox { IsChecked = current, VerticalAlignment = VerticalAlignment.Center };
        chk.Checked += (_, _) => onChange(true);
        chk.Unchecked += (_, _) => onChange(false);
        return Row(label, chk);
    }

    private static FrameworkElement Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(lbl);
        grid.Children.Add(control);
        return grid;
    }
}
