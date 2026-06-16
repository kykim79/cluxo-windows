using Cluxo.Windows.App.Shell;
using Microsoft.Win32;

namespace Cluxo.Windows.App.Tests;

// HKCU Run 키 토글 (SHELL-LAYER.md §4). 실제 레지스트리를 건드리므로 고유 값 이름 + 확실한 정리.
public sealed class RegistryLaunchAtLoginTests : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName = "CluxoTest-" + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    [Fact]
    public void Toggle_EnablesAndDisables()
    {
        var launch = new RegistryLaunchAtLogin(_valueName);
        Assert.False(launch.IsEnabled); // 초기엔 없음

        launch.IsEnabled = true;
        Assert.True(launch.IsEnabled);
        // 실제 값이 따옴표로 감싼 실행 경로인지 확인
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
        {
            var data = key?.GetValue(_valueName) as string;
            Assert.False(string.IsNullOrEmpty(data));
            Assert.StartsWith("\"", data);
        }

        launch.IsEnabled = false;
        Assert.False(launch.IsEnabled);
    }

    [Fact]
    public void Disable_WhenAbsent_NoThrow()
    {
        var launch = new RegistryLaunchAtLogin(_valueName);
        launch.IsEnabled = false; // 없는 값 삭제 — throwOnMissingValue:false
        Assert.False(launch.IsEnabled);
    }
}
