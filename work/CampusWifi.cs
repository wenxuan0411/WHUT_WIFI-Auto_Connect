using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed class WifiNetwork
{
    internal Guid Adapter;
    internal string Ssid;
    internal string Profile;
    internal bool Connected;
    internal bool Connectable;
    internal bool Secure;
    internal uint Signal;
}

internal interface IWifiController
{
    List<WifiNetwork> GetNetworks(bool scan);
    bool Connect(WifiNetwork network);
}

static class CampusWifi
{
    internal static bool IsTarget(string ssid)
    {
        return ssid == "WHUT-DORM" || ssid == "WHUT-WLAN";
    }

    internal static string EnsureConnected()
    {
        using (var gate = new Mutex(false, @"Local\CampusFlow-WHUT.WifiConnect"))
        {
            bool acquired;
            try { acquired = gate.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return "已有校园 Wi-Fi 连接任务正在运行";
            try
            {
                using (var wifi = new NativeWifi()) return EnsureConnected(wifi, Thread.Sleep);
            }
            catch (Win32Exception ex)
            {
                string message = ex.NativeErrorCode == 5
                    ? "Wi-Fi 访问被拒绝，请在 Windows 设置中开启定位服务并允许桌面应用访问位置"
                    : "Wi-Fi 操作失败（错误码 " + ex.NativeErrorCode + "），请检查无线开关和 WLAN AutoConfig 服务";
                AppData.Log(message);
                return message;
            }
            finally { gate.ReleaseMutex(); }
        }
    }

    // null 表示已关联目标 Wi-Fi；外网认证由 PortalClient 继续处理。
    internal static string EnsureConnected(IWifiController wifi, Action<int> wait)
    {
        if (wifi.GetNetworks(false).Any(n => n.Connected && IsTarget(n.Ssid))) return null;
        List<WifiNetwork> available = wifi.GetNetworks(true);
        // 扫描期间 Windows 可能已经自动关联，避免再次打断连接。
        if (available.Any(n => n.Connected && IsTarget(n.Ssid))) return null;
        var candidates = available.Where(n => IsTarget(n.Ssid) && n.Connectable &&
            (!n.Secure || !String.IsNullOrEmpty(n.Profile)))
            .OrderBy(n => n.Ssid == "WHUT-DORM" ? 0 : 1).ThenByDescending(n => n.Signal)
            .GroupBy(n => new { n.Adapter, n.Ssid }).Select(group => group.First()).ToList();
        foreach (WifiNetwork network in candidates)
        {
            AppData.Log("正在连接校园 Wi-Fi：" + network.Ssid);
            if (!wifi.Connect(network)) continue;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                wait(1000);
                if (wifi.GetNetworks(false).Any(n => n.Adapter == network.Adapter && n.Connected && n.Ssid == network.Ssid))
                {
                    AppData.Log("已连接校园 Wi-Fi：" + network.Ssid);
                    wait(2000); // 留出 DHCP 获取地址的时间，未就绪时由后台下一轮重试。
                    return null;
                }
            }
        }
        string error = candidates.Count == 0
            ? "未发现可连接的 WHUT-DORM / WHUT-WLAN，将按间隔重新扫描"
            : "校园 Wi-Fi 连接未成功，将按间隔重试";
        AppData.Log(error);
        return error;
    }
}

// 使用 Windows Native Wi-Fi API，避免依赖 netsh 输出的系统语言。
// 复用已有配置；开放网络无配置时使用临时配置，不删除或覆盖用户配置。
internal sealed class NativeWifi : IWifiController, IDisposable
{
    IntPtr handle;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct InterfaceInfo
    {
        internal Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Description;
        internal uint State;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Ssid
    {
        internal uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Profile;
        internal Ssid Ssid;
        internal uint BssType, BssidCount;
        internal int Connectable;
        internal uint Reason, PhyCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] internal uint[] PhyTypes;
        internal int MorePhyTypes;
        internal uint Signal;
        internal int Secure;
        internal uint Auth, Cipher, Flags, Reserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ConnectionParameters
    {
        internal uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] internal string Profile;
        internal IntPtr Ssid, BssidList;
        internal uint BssType, Flags;
    }

    [DllImport("wlanapi.dll")] static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr client);
    [DllImport("wlanapi.dll")] static extern uint WlanCloseHandle(IntPtr client, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")] static extern uint WlanEnumInterfaces(IntPtr client, IntPtr reserved, out IntPtr interfaces);
    [DllImport("wlanapi.dll")] static extern uint WlanScan(IntPtr client, ref Guid id, IntPtr ssid, IntPtr ieData, IntPtr reserved);
    [DllImport("wlanapi.dll")] static extern uint WlanGetAvailableNetworkList(IntPtr client, ref Guid id, uint flags, IntPtr reserved, out IntPtr networks);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] static extern uint WlanConnect(IntPtr client, ref Guid id, ref ConnectionParameters parameters, IntPtr reserved);

    internal NativeWifi()
    {
        uint version;
        Check(WlanOpenHandle(2, IntPtr.Zero, out version, out handle));
    }

    static void Check(uint error)
    {
        if (error != 0) throw new Win32Exception((int)error);
    }

    public List<WifiNetwork> GetNetworks(bool scan)
    {
        var adapters = new List<Guid>();
        IntPtr interfaces;
        Check(WlanEnumInterfaces(handle, IntPtr.Zero, out interfaces));
        try
        {
            int count = Marshal.ReadInt32(interfaces);
            int size = Marshal.SizeOf(typeof(InterfaceInfo));
            for (int i = 0; i < count; i++)
            {
                var adapter = (InterfaceInfo)Marshal.PtrToStructure(IntPtr.Add(interfaces, 8 + i * size), typeof(InterfaceInfo));
                adapters.Add(adapter.Id);
            }
        }
        finally { WlanFreeMemory(interfaces); }
        if (adapters.Count == 0) throw new Win32Exception(1168); // 当前没有可用无线网卡。
        uint lastError = 0;
        bool scanning = false;
        if (scan)
        {
            foreach (Guid adapter in adapters)
            {
                Guid id = adapter;
                uint error = WlanScan(handle, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (error == 0) scanning = true;
                else lastError = error;
            }
            if (!scanning) Check(lastError);
            Thread.Sleep(4000); // WlanScan 异步返回；驱动完成扫描最长约 4 秒。
        }
        var result = new List<WifiNetwork>();
        bool queried = false;
        foreach (Guid adapter in adapters)
        {
            Guid id = adapter;
            IntPtr networks;
            uint error = WlanGetAvailableNetworkList(handle, ref id, 0, IntPtr.Zero, out networks);
            if (error != 0) { lastError = error; continue; }
            queried = true;
            try
            {
                int count = Marshal.ReadInt32(networks);
                int size = Marshal.SizeOf(typeof(AvailableNetwork));
                for (int i = 0; i < count; i++)
                {
                    var entry = (AvailableNetwork)Marshal.PtrToStructure(IntPtr.Add(networks, 8 + i * size), typeof(AvailableNetwork));
                    if (entry.BssType != 1 || entry.Ssid.Length > 32) continue;
                    string name = System.Text.Encoding.UTF8.GetString(entry.Ssid.Bytes, 0, (int)entry.Ssid.Length);
                    result.Add(new WifiNetwork { Adapter = id, Ssid = name, Profile = entry.Profile,
                        Connected = (entry.Flags & 1) != 0, Connectable = entry.Connectable != 0,
                        Secure = entry.Secure != 0, Signal = entry.Signal });
                }
            }
            finally { WlanFreeMemory(networks); }
        }
        if (!queried) Check(lastError);
        return result;
    }

    public bool Connect(WifiNetwork network)
    {
        if (!CampusWifi.IsTarget(network.Ssid)) throw new InvalidOperationException("仅允许连接指定校园 Wi-Fi");
        bool saved = !String.IsNullOrEmpty(network.Profile);
        if (network.Secure && !saved) return false;
        var parameters = new ConnectionParameters
        {
            Mode = saved ? 0u : 1u,
            Profile = saved ? network.Profile : OpenProfile(network.Ssid),
            BssType = 1
        };
        Guid id = network.Adapter;
        uint error = WlanConnect(handle, ref id, ref parameters, IntPtr.Zero);
        if (error == 5) Check(error);
        if (error != 0) AppData.Log("校园 Wi-Fi 连接请求失败，错误码：" + error);
        return error == 0;
    }

    internal static string OpenProfile(string ssid)
    {
        if (!CampusWifi.IsTarget(ssid)) throw new InvalidOperationException("无效校园 SSID");
        return "<?xml version=\"1.0\"?><WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
            "<name>" + ssid + "</name><SSIDConfig><SSID><name>" + ssid + "</name></SSID></SSIDConfig>" +
            "<connectionType>ESS</connectionType><connectionMode>manual</connectionMode><MSM><security>" +
            "<authEncryption><authentication>open</authentication><encryption>none</encryption>" +
            "<useOneX>false</useOneX></authEncryption></security></MSM></WLANProfile>";
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero) { WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; }
    }
}
