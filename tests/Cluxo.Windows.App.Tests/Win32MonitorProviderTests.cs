using Cluxo.Windows.App.Shell;

namespace Cluxo.Windows.App.Tests;

// EnumDisplayMonitors + DPI 구조체 마샬링 검증 (SHELL-LAYER.md §7).
// CI 헤드리스에서 모니터 0개일 수 있어 개수는 강제하지 않고, 반환된 항목의 구조만 검증.
public class Win32MonitorProviderTests
{
    [Fact]
    public void Enumerate_DoesNotThrow_ReturnsList()
    {
        var monitors = Win32MonitorProvider.Enumerate();
        Assert.NotNull(monitors);
    }

    [Fact]
    public void Enumerate_ReturnedMonitors_AreStructurallyValid()
    {
        var monitors = Win32MonitorProvider.Enumerate();

        foreach (var m in monitors)
        {
            Assert.False(string.IsNullOrEmpty(m.Id));          // szDevice
            Assert.True(m.Bounds.Width > 0 && m.Bounds.Height > 0, $"bounds={m.Bounds}");
            Assert.True(m.DpiScale > 0, $"dpi={m.DpiScale}");
        }

        // 모니터가 있으면 주 모니터는 정확히 하나
        if (monitors.Count > 0)
            Assert.Equal(1, monitors.Count(m => m.IsPrimary));
    }
}
