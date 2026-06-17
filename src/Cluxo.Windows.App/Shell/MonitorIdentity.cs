using System.Runtime.InteropServices;

namespace Cluxo.Windows.App.Shell;

/// <summary>
/// 현재 연결된 모니터의 안정 식별자 + 표시 이름. (맥 MonitorIdentity 대응)
/// EnumDisplayDevices로 모니터의 EDID 기반 device interface name을 얻는다 — szDevice(\\.\DISPLAY1)와 달리
/// 재배열·재연결에도 대체로 유지돼 신뢰 모니터 매칭에 적합. (저가 어댑터는 generated라 변할 수 있음.)
/// </summary>
internal static class MonitorIdentity
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x1;

    /// <summary>현재 연결된 모니터들의 (안정 ID, 표시 이름).</summary>
    public static List<(string Id, string Name)> Connected()
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>();
        for (uint i = 0; ; i++)
        {
            var adapter = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(adapter.DeviceName, 0, ref mon, EDD_GET_DEVICE_INTERFACE_NAME)) continue;

            string id = string.IsNullOrEmpty(mon.DeviceID) ? adapter.DeviceName : mon.DeviceID;
            if (!seen.Add(id)) continue; // 중복 제거
            string name = string.IsNullOrWhiteSpace(mon.DeviceString) ? "모니터" : mon.DeviceString;
            result.Add((id, name));
        }
        return result;
    }
}
