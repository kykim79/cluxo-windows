using Cluxo.Core.Platform;
using Microsoft.Win32;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// <see cref="ILaunchAtLogin"/> — HKCU Run 키(SHELL-LAYER.md §4). 사용자별이라 관리자 권한 불필요.
/// 값 이름은 "Cluxo", 데이터는 따옴표로 감싼 실행 파일 경로.
/// </summary>
public sealed class RegistryLaunchAtLogin : ILaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName;

    public RegistryLaunchAtLogin(string valueName = "Cluxo") => _valueName = valueName;

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(_valueName) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return; // 경로 불명이면 no-op
                key.SetValue(_valueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(_valueName, throwOnMissingValue: false);
            }
        }
    }
}
