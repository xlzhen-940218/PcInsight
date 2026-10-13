using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PcInsight.Core;

/// <summary>
/// Hardware collectors — devices: graphics, network, audio, battery, USB, printers,
/// security and sensors. The other half of this class lives in Hardware.cs.
/// </summary>
public static partial class Hardware
{
    private const string BitLockerNs = @"root\cimv2\Security\MicrosoftVolumeEncryption";

    private static bool Zh => Babel.L.IsChinese;

    private static string Pair(string zh, string en) => Zh ? zh : en;

    // ================================================================== graphics & display
    public static Section Graphics()
    {
        var s = new Section("graphics", "nav.graphics", "\uE7F4");
        var controllers = Wmi.Query("SELECT * FROM Win32_VideoController");
        var monitors = Native.Monitors();
        var edids = Native.Edids();
        var desktopMonitors = Wmi.Query("SELECT * FROM Win32_DesktopMonitor");

        foreach (var vc in controllers)
        {
            var g = s.AddGroup("grp.gpu");
            var name = vc.StrOrNull("Name");
            g.Subtitle = name ?? string.Empty;

            var vram = vc.U64("AdapterRAM");
            var reported = vram is { } v && v > 0 && v != uint.MaxValue && v < 128UL * 1024 * 1024 * 1024 ? Fmt.Size(v) : null;

            g
                .AddIf("f.gpuName", name)
                .AddIf("f.gpuVendor", vc.StrOrNull("AdapterCompatibility"))
                .AddIf("f.gpuDedicated", DedicatedMemory(name))
                .AddIf("f.gpuAdapterRam", reported)
                .AddIf("f.gpuDriver", vc.StrOrNull("DriverVersion"))
                .Add("f.gpuDriverDate", Fmt.DateOnly(vc.StrOrNull("DriverDate")))
                .AddIf("f.gpuProcessor", vc.StrOrNull("VideoProcessor"))
                .AddIf("f.gpuBios", vc.StrOrNull("VideoBIOSVersion") ?? vc.StrOrNull("BIOSVersion"))
                .AddIf("f.gpuArchitecture", Lookup.VideoArchitecture(vc.I32("VideoArchitecture")))
                .AddIf("f.gpuResolution", ResolutionOf(vc, monitors))
                .AddIf("f.gpuRefresh", RefreshOf(vc, monitors))
                .AddIf("f.gpuBits", BitsOf(vc, monitors))
                .AddIf("f.gpuPnp", vc.StrOrNull("PNPDeviceID"))
                .AddIf("f.gpuStatus", Lookup.Availability(3), tone: Tone.Good);
        }

        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var g = s.AddGroup("grp.display");
            var edid = i < edids.Count ? edids[i] : null;
            var friendly = desktopMonitors.Count > i ? desktopMonitors[i].StrOrNull("Name") : null;
            g.Subtitle = friendly ?? m.DeviceName;

            g
                .AddIf("f.monitorName", friendly ?? m.DeviceName)
                .AddIf("f.monitorId", m.DeviceId)
                .Add("f.monitorResolution", m.Width > 0 ? m.Width + " × " + m.Height : Fmt.Unknown)
                .AddIf("f.monitorRefresh", m.RefreshHz > 0 ? m.RefreshHz + " Hz" : null)
                .AddIf("f.gpuBits", m.BitsPerPixel > 0 ? m.BitsPerPixel + "-bit" : null)
                .AddIf("f.monitorDpi", m.Dpi + " DPI · " + m.ScalePercent + " %")
                .Add("f.monitorPrimary", Fmt.B(m.Primary))
                .AddIf("f.monitorSize", edid?.SizeText)
                .AddIf("f.monitorYear", edid is null ? null : edid.Year.ToString(CultureInfo.CurrentCulture))
                .AddIf("f.monitorWeek", edid is null ? null : edid.Week.ToString(CultureInfo.CurrentCulture))
                .AddIf("f.monitorEdid", edid?.Version)
                .AddIf("f.hdrEnabled", edid is null ? null : Fmt.B(edid.Hdr));
        }

        var env = s.AddGroup("grp.graphicsEnv");
        env
            .Add("f.gpuCount", monitors.Count.ToString(CultureInfo.CurrentCulture))
            .AddIf("f.gpuName", controllers.Count > 0 ? string.Join(" / ", controllers.Select(c => c.StrOrNull("Name")).Where(Fmt.Has)) : null)
            .AddIf("f.monitorResolution", SystemParameters())
            .AddIf("f.hdrEnabled", HdrEnabled())
            .AddIf("f.directX", Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\DirectX", "Version", view64: true));

        s.Summary = Fmt.Or(controllers.FirstOrDefault()?.StrOrNull("Name"), Fmt.Unknown)
            + (monitors.Count > 1 ? " · " + monitors.Count + (Zh ? " 台显示器" : " displays") : string.Empty)
            + (monitors.Count == 1 ? " · " + monitors[0].Width + "×" + monitors[0].Height : string.Empty);
        return s;
    }

    private static string? DedicatedMemory(string? gpuName)
    {
        if (!Fmt.Has(gpuName))
        {
            return null;
        }

        var key = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        foreach (var sub in Reg.SubKeys(RegistryHive.LocalMachine, key))
        {
            if (!sub.All(char.IsDigit))
            {
                continue;
            }

            var desc = Reg.Value(RegistryHive.LocalMachine, key + "\\" + sub, "DriverDesc");
            if (!Fmt.Has(desc) || !desc!.Contains(gpuName!.Split(' ')[0], StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var qw = Reg.Value(RegistryHive.LocalMachine, key + "\\" + sub, "HardwareInformation.qwMemorySize");
            if (long.TryParse(qw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes > 0)
            {
                return Fmt.Size(bytes);
            }

            var mem = Reg.Value(RegistryHive.LocalMachine, key + "\\" + sub, "HardwareInformation.MemorySize");
            if (long.TryParse(mem, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m2) && m2 > 0)
            {
                return Fmt.Size(m2);
            }
        }

        return null;
    }

    private static string? ResolutionOf(WmiRow vc, List<Native.MonitorInfo> monitors)
    {
        var w = vc.U32("CurrentHorizontalResolution");
        var h = vc.U32("CurrentVerticalResolution");
        if (w is > 0 && h is > 0)
        {
            return w + " × " + h;
        }

        return null;
    }

    private static string? RefreshOf(WmiRow vc, List<Native.MonitorInfo> monitors)
    {
        var hz = vc.U32("CurrentRefreshRate");
        if (hz is > 0)
        {
            return hz + " Hz";
        }

        var fromNative = monitors.FirstOrDefault(m => m.RefreshHz > 0)?.RefreshHz;
        return fromNative is > 0 ? fromNative + " Hz" : null;
    }

    private static string? BitsOf(WmiRow vc, List<Native.MonitorInfo> monitors)
    {
        var bits = vc.U32("CurrentBitsPerPixel");
        if (bits is > 0)
        {
            return bits + "-bit";
        }

        var b = monitors.FirstOrDefault(m => m.BitsPerPixel > 0)?.BitsPerPixel;
        return b is > 0 ? b + "-bit" : null;
    }

    private static string? SystemParameters()
    {
        return null;
    }

    private static string? HdrEnabled()
    {
        try
        {
            var key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Configuration";
            foreach (var sub in Reg.SubKeys(RegistryHive.LocalMachine, key).Take(8))
            {
                foreach (var child in Reg.SubKeys(RegistryHive.LocalMachine, key + "\\" + sub).Take(4))
                {
                    var v = Reg.Value(RegistryHive.LocalMachine, key + "\\" + sub + "\\" + child, "AdvancedColorEnabled");
                    if (v == "1")
                    {
                        return Fmt.Yes;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    // ================================================================== network
    public static Section Network()
    {
        var s = new Section("network", "nav.network", "\uE968");
        var adapters = Wmi.Query("SELECT * FROM Win32_NetworkAdapter");
        var configs = Wmi.Query("SELECT * FROM Win32_NetworkAdapterConfiguration")
            .ToDictionary(c => c.Str("Index"), c => c, StringComparer.OrdinalIgnoreCase);
        var activeConfigs = Wmi.Query("SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE");

        var wifi = WifiState();

        var active = s.AddGroup("grp.netActive");
        active
            .AddIf("f.netHostname", Environment.MachineName)
            .AddIf("f.netSsid", wifi?.Ssid)
            .AddIf("f.netSignal", wifi?.Signal)
            .AddIf("f.netAdapterCount", adapters.Count(a => a.B("PhysicalAdapter") == true).ToString(CultureInfo.CurrentCulture));

        var shown = 0;
        foreach (var adapter in adapters.Where(a => a.B("PhysicalAdapter") == true).OrderByDescending(a => a.I32("NetConnectionStatus") == 2))
        {
            var name = adapter.StrOrNull("NetConnectionID") ?? adapter.StrOrNull("Name");
            var description = adapter.StrOrNull("Name") ?? adapter.StrOrNull("Description");
            var g = s.AddGroup("grp.netAdapter");
            g.Subtitle = Fmt.Or(description, Fmt.Unknown);

            var type = adapter.I32("AdapterTypeID");
            var isWifi = adapter.I32("NdisMedium") == 9
                || (description?.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ?? false)
                || (description?.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ?? false)
                || (description?.Contains("802.11", StringComparison.OrdinalIgnoreCase) ?? false);

            var up = adapter.I32("NetConnectionStatus") == 2;
            configs.TryGetValue(adapter.Str("Index"), out var cfg);

            g
                .AddIf("f.netName", name)
                .AddIf("f.netDescription", description)
                .AddIf("f.netType", isWifi ? Pair("无线 (Wi-Fi)", "Wireless (Wi-Fi)") : Lookup.NetType(type))
                .AddIf("f.netWifiStandard", isWifi ? Lookup.WifiStandard(description) : null)
                .Add("f.netStatus", Lookup.NetConnectionStatus(adapter.I32("NetConnectionStatus"), up), tone: up ? Tone.Good : Tone.Muted)
                .AddIf("f.netSpeed", Lookup.LinkSpeed(adapter.StrOrNull("Speed")))
                .AddIf("f.netMac", FormatMac(adapter.StrOrNull("MACAddress")))
                .AddIf("f.netIpv4", cfg is null ? null : Fmt.Join(cfg.Array("IPAddress").Where(IsIpv4)))
                .AddIf("f.netIpv6", cfg is null ? null : Fmt.Join(cfg.Array("IPAddress").Where(a => !IsIpv4(a)).Select(ShortenIpv6)))
                .AddIf("f.netSubnet", cfg is null ? null : Fmt.Join(cfg.Array("IPSubnet").Where(IsIpv4)))
                .AddIf("f.netGateway", cfg is null ? null : Fmt.Join(cfg.Array("DefaultIPGateway")))
                .AddIf("f.netDns", cfg is null ? null : Fmt.Join(cfg.Array("DNSServerSearchOrder")))
                .AddIf("f.netDhcp", cfg?.B("DHCPEnabled") is { } dhcp ? (dhcp ? (Zh ? "已启用" : "Enabled") : (Zh ? "静态 (未启用)" : "Static")) : null)
                .AddIf("f.netDhcpServer", cfg is null ? null : Fmt.Join(cfg.Array("DHCPServer")))
                .AddIf("f.netMtu", cfg?.U32("MTU")?.ToString(CultureInfo.CurrentCulture))
                .AddIf("f.netAdapterStatus", Lookup.Availability(adapter.I32("Availability")), tone: Lookup.AvailabilityTone(adapter.I32("Availability")));
            shown++;
        }

        if (shown == 0)
        {
            foreach (var adapter in adapters.Where(a => a.I32("NetConnectionStatus") is not null).Take(4))
            {
                var g = s.AddGroup("grp.netAdapter");
                g.Subtitle = Fmt.Or(adapter.StrOrNull("Name"), Fmt.Unknown);
                configs.TryGetValue(adapter.Str("Index"), out var cfg);
                g.AddIf("f.netName", adapter.StrOrNull("NetConnectionID"))
                    .AddIf("f.netDescription", adapter.StrOrNull("Name"))
                    .Add("f.netStatus", Lookup.NetConnectionStatus(adapter.I32("NetConnectionStatus"), adapter.I32("NetConnectionStatus") == 2))
                    .AddIf("f.netMac", FormatMac(adapter.StrOrNull("MACAddress")))
                    .AddIf("f.netIpv4", cfg is null ? null : Fmt.Join(cfg.Array("IPAddress")));
            }
        }

        var dns = DnsState();
        var extra = s.AddGroup("grp.netActive");
        extra.Items.Clear();
        extra
            .AddIf("f.netDnsHost", dns?.HostName)
            .AddIf("f.netPublicIp", dns?.Addresses)
            .AddIf("f.netFirewall", FirewallState());

        var primary = activeConfigs.FirstOrDefault();
        s.Summary = primary is null
            ? (wifi?.Ssid is { } ssid ? ssid : Fmt.Unknown)
            : Fmt.Join(new[] { Fmt.Join(primary.Array("IPAddress").Where(IsIpv4)), primary.StrOrNull("Description") }, " · ");
        return s;
    }

    private static bool IsIpv4(string ip) => ip.Contains('.') && !ip.Contains(':');

    private static string ShortenIpv6(string ip)
    {
        var cut = ip.IndexOf('%');
        return cut > 0 ? ip[..cut] : ip;
    }

    private static string? FormatMac(string? mac)
    {
        if (!Fmt.Has(mac))
        {
            return null;
        }

        var clean = mac!.Replace(":", string.Empty).Replace("-", string.Empty).Trim();
        if (clean.Length != 12)
        {
            return mac;
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => clean.Substring(i * 2, 2).ToUpperInvariant()));
    }

    private sealed record WifiInfo(string? Ssid, string? Signal);

    private static WifiInfo? WifiState()
    {
        try
        {
            var networks = Wmi.Query("SELECT * FROM MSNdis_80211_ServiceSetIdentifier", @"\\.\root\wmi");
            var ssid = networks
                .Select(n => n.StrOrNull("Ndis80211SsId"))
                .FirstOrDefault(Fmt.Has);
            return ssid is null ? null : new WifiInfo(ssid, null);
        }
        catch
        {
            return null;
        }
    }

    private sealed record DnsInfo(string? HostName, string? Addresses);

    private static DnsInfo? DnsState()
    {
        try
        {
            var host = System.Net.Dns.GetHostName();
            var entries = System.Net.Dns.GetHostEntry(host);
            var v4 = entries.AddressList
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();
            return new DnsInfo(host, v4.Count > 0 ? string.Join(", ", v4) : null);
        }
        catch
        {
            return new DnsInfo(Environment.MachineName, null);
        }
    }

    private static string? FirewallState()
    {
        var states = new List<string>();
        foreach (var profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
        {
            var v = Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profile, "EnableFirewall");
            var label = profile switch
            {
                "DomainProfile" => Pair("域", "Domain"),
                "StandardProfile" => Pair("专用", "Private"),
                _ => Pair("公用", "Public"),
            };
            if (v is not null)
            {
                states.Add(label + ": " + (v.Value != 0 ? Pair("开启", "On") : Pair("关闭", "Off")));
            }
        }

        return states.Count == 0 ? null : string.Join(" · ", states);
    }

    // ================================================================== audio
    public static Section Audio()
    {
        var s = new Section("audio", "nav.audio", "\uE8D6");
        var devices = Wmi.Query("SELECT * FROM Win32_SoundDevice");

        foreach (var d in devices)
        {
            var g = s.AddGroup("grp.audioDevice");
            g.Subtitle = Fmt.Or(d.StrOrNull("Name"), Fmt.Unknown);
            g
                .AddIf("f.audioName", d.StrOrNull("Name"))
                .AddIf("f.audioMfr", d.StrOrNull("Manufacturer"))
                .AddIf("f.audioKind", Pair("声音设备", "Sound device"))
                .AddIf("f.audioStatus", Lookup.Availability(d.I32("StatusInfo") is null ? 3 : 3), tone: Tone.Good)
                .AddIf("f.audioDriver", DriverVersionOf(d.StrOrNull("DeviceID")));
        }

        foreach (var (dev, i) in MediaDevices().Select((d, i) => (d, i)))
        {
            var g = s.AddGroup("grp.audioDevice");
            g.Subtitle = dev;
            g
                .AddIf("f.audioName", dev)
                .AddIf("f.audioKind", Pair("播放 / 录音端点", "Playback / recording endpoint"))
                .AddIf("f.audioStatus", Pair("可用", "Available"), tone: Tone.Good);
        }

        s.Summary = devices.Count == 0
            ? Fmt.Unknown
            : string.Join(" · ", devices.Select(d => d.StrOrNull("Name")).Where(Fmt.Has));
        return s;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int waveOutGetNumDevs();

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int waveInGetNumDevs();

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int waveOutGetDevCaps(IntPtr deviceId, ref WAVEOUTCAPS caps, int size);

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int waveInGetDevCaps(IntPtr deviceId, ref WAVEINCAPS caps, int size);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WAVEOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;

        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;

        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;

        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }

    private static List<string> MediaDevices()
    {
        var list = new List<string>();
        try
        {
            var count = waveOutGetNumDevs();
            for (var i = 0; i < count; i++)
            {
                var caps = new WAVEOUTCAPS();
                if (waveOutGetDevCaps(new IntPtr(i), ref caps, System.Runtime.InteropServices.Marshal.SizeOf<WAVEOUTCAPS>()) == 0 && Fmt.Has(caps.szPname))
                {
                    list.Add(caps.szPname.Trim());
                }
            }

            var inCount = waveInGetNumDevs();
            for (var i = 0; i < inCount; i++)
            {
                var caps = new WAVEINCAPS();
                if (waveInGetDevCaps(new IntPtr(i), ref caps, System.Runtime.InteropServices.Marshal.SizeOf<WAVEINCAPS>()) == 0 && Fmt.Has(caps.szPname))
                {
                    list.Add(caps.szPname.Trim());
                }
            }
        }
        catch
        {
            // ignore
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ================================================================== battery & power
    public static Section Battery()
    {
        var s = new Section("battery", "nav.battery", "\uE83F");
        var batteries = Wmi.Query("SELECT * FROM Win32_Battery");
        var status = Native.Power();

        var index = 1;
        foreach (var b in batteries)
        {
            var g = s.AddGroup("grp.battery");
            var name = b.StrOrNull("Name") ?? b.StrOrNull("DeviceID");
            g.Subtitle = Fmt.Or(b.StrOrNull("Name"), Pair("电池 " + index, "Battery " + index));
            g
                .AddIf("f.batDeviceId", b.StrOrNull("DeviceID"))
                .AddIf("f.batChemistry", Lookup.BatteryChemistry(b.I32("Chemistry")))
                .Add("f.batCharge", b.I32("EstimatedChargeRemaining") is { } pct
                    ? pct.ToString(CultureInfo.CurrentCulture) + " %"
                    : (status?.BatteryPercent is { } p2 ? p2.ToString(CultureInfo.CurrentCulture) + " %" : Fmt.Unknown))
                .AddIf("f.batVoltage", b.U32("DesignVoltage") is { } mv and > 0 ? (mv / 1000.0).ToString("N2", CultureInfo.CurrentCulture) + " V" : null)
                .AddIf("f.batRate", b.I32("EstimatedRunTime") is { } rt and > 0 ? Fmt.Duration(TimeSpan.FromMinutes(rt)) : null)
                .AddIf("f.batTimeRemaining", status?.Remaining is { } rem ? Fmt.Duration(rem) : null)
                .Add("f.batAc", status?.AcOnline is { } ac ? Fmt.B(ac) : Fmt.Unknown)
                .AddIf("f.batStatus", Lookup.BatteryStatus(b.I32("BatteryStatus")))
                .AddIf("f.batCycle", CycleCount(b.StrOrNull("DeviceID")));
            index++;
        }

        var power = s.AddGroup("grp.power");
        var plan = Wmi.Query("SELECT * FROM Win32_PowerPlan", @"\\.\root\cimv2\power")
            .FirstOrDefault(p => p.B("IsActive") == true);
        power
            .AddIf("f.powerPlanActive", plan?.StrOrNull("ElementName"))
            .AddIf("f.batAc", status?.AcOnline is { } ac2 ? (ac2 ? Pair("已接通", "Connected") : Pair("未接通", "On battery")) : null)
            .AddIf("f.powerSleep", Reg.Value(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Power", "HiberFileType"));

        s.Summary = status?.BatteryPercent is { } charge
            ? charge + " %" + (status.AcOnline == true ? " · " + Pair("已接通电源", "on AC") : " · " + Pair("使用电池", "on battery"))
            : (batteries.Count == 0 ? Pair("未检测到电池（台式机）", "No battery detected (desktop)") : Fmt.Unknown);
        s.Severity = status?.BatteryPercent is { } c && c <= 15 && status.AcOnline == false ? Condition.Warn : Condition.Ok;
        return s;
    }

    private static string? CycleCount(string? deviceId)
    {
        try
        {
            foreach (var row in Wmi.Query("SELECT * FROM BatteryCycleCount", @"\\.\root\wmi"))
            {
                var count = row.U32("CycleCount");
                if (count is not null)
                {
                    return count.Value.ToString(CultureInfo.CurrentCulture);
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    // ================================================================== USB & peripherals
    public static Section Usb()
    {
        var s = new Section("usb", "nav.usb", "\uE88E");
        var entities = Wmi.Query("SELECT * FROM Win32_PnPEntity");

        var usb = entities
            .Where(e => (e.StrOrNull("PNPDeviceID") ?? string.Empty).StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.StrOrNull("Name"))
            .ToList();

        var active = s.AddGroup("grp.usbDevice");
        active.Items.Clear();
        active.AddIf("f.usbCount", usb.Count.ToString(CultureInfo.CurrentCulture));

        foreach (var e in usb)
        {
            var name = e.StrOrNull("Name");
            if (!Fmt.Has(name))
            {
                continue;
            }

            var g = s.AddGroup("grp.usbDevice");
            var present = e.B("ConfigManagerErrorCode") is null || e.I32("ConfigManagerErrorCode") == 0;
            g.Subtitle = name!;
            g
                .AddIf("f.usbName", name)
                .Add("f.usbConnected", Fmt.B(present), tone: present ? Tone.Good : Tone.Warn)
                .AddIf("f.usbClass", e.StrOrNull("PNPClass"))
                .AddIf("f.usbService", e.StrOrNull("Service"))
                .AddIf("f.usbStatus", Lookup.ErrorLevel(e.I32("ConfigManagerErrorCode")), tone: Lookup.ErrorLevelTone(e.I32("ConfigManagerErrorCode")))
                .AddIf("f.usbVendor", e.StrOrNull("Manufacturer"))
                .AddIf("f.usbProduct", ParseVidPid(e.StrOrNull("PNPDeviceID")));
        }

        var controllers = entities
            .Where(e => (e.StrOrNull("PNPClass") ?? string.Empty).Contains("USB", StringComparison.OrdinalIgnoreCase)
                        && (e.StrOrNull("Name") ?? string.Empty).Contains("Controller", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var c in controllers)
        {
            var g = s.AddGroup("grp.usbController");
            g.Subtitle = Fmt.Or(c.StrOrNull("Name"), Fmt.Unknown);
            g
                .AddIf("f.usbName", c.StrOrNull("Name"))
                .AddIf("f.usbStatus", Lookup.ErrorLevel(c.I32("ConfigManagerErrorCode")), tone: Lookup.ErrorLevelTone(c.I32("ConfigManagerErrorCode")))
                .AddIf("f.usbVendor", c.StrOrNull("Manufacturer"));
        }

        // a few other peripheral classes worth listing
        foreach (var cls in new[] { "Bluetooth", "SmartCardReader", "Camera", "Keyboard", "Mouse", "HIDClass" })
        {
            var devs = entities
                .Where(e => string.Equals(e.StrOrNull("PNPClass"), cls, StringComparison.OrdinalIgnoreCase))
                .Where(e => Fmt.Has(e.StrOrNull("Name")))
                .Take(12)
                .ToList();
            foreach (var d in devs)
            {
                var g = s.AddGroup("grp.pnpOther");
                g.Subtitle = (d.StrOrNull("PNPClass") ?? cls) + " · " + (d.StrOrNull("Name") ?? Fmt.Unknown);
                g
                    .AddIf("f.usbName", d.StrOrNull("Name"))
                    .AddIf("f.usbClass", d.StrOrNull("PNPClass"))
                    .AddIf("f.usbStatus", Lookup.ErrorLevel(d.I32("ConfigManagerErrorCode")), tone: Lookup.ErrorLevelTone(d.I32("ConfigManagerErrorCode")))
                    .AddIf("f.usbProduct", ParseVidPid(d.StrOrNull("PNPDeviceID")));
            }
        }

        s.Summary = usb.Count + (Zh ? " 个 USB 设备" : " USB devices");
        return s;
    }

    private static string? ParseVidPid(string? pnpId)
    {
        if (!Fmt.Has(pnpId) || !pnpId!.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = pnpId.Split('\\');
        return parts.Length >= 2 ? parts[1].Replace("VID_", "VID ").Replace("PID_", "PID ") : null;
    }

    // ================================================================== printers
    public static Section Printers()
    {
        var s = new Section("printers", "nav.printers", "\uE749");
        var printers = Wmi.Query("SELECT * FROM Win32_Printer");

        foreach (var p in printers)
        {
            var g = s.AddGroup("grp.printer");
            var name = p.StrOrNull("Name");
            g.Subtitle = Fmt.Or(name, Fmt.Unknown);
            g
                .AddIf("f.printerName", name)
                .AddIf("f.printerDriver", p.StrOrNull("DriverName"))
                .AddIf("f.printerPort", p.StrOrNull("PortName"))
                .AddIf("f.printerDefault", p.B("Default") == true ? Fmt.Yes : null)
                .AddIf("f.printerShared", p.B("Shared") is { } sh ? Fmt.B(sh) : null)
                .AddIf("f.printerStatus", PrinterStatus(p), tone: p.B("WorkOffline") == true ? Tone.Muted : Tone.Normal);
        }

        s.Summary = printers.Count == 0
            ? Pair("未安装打印机", "No printers installed")
            : string.Join(" · ", printers.Select(p => p.StrOrNull("Name")).Where(Fmt.Has));
        return s;
    }

    private static string? PrinterStatus(WmiRow p)
    {
        var states = new List<string>();
        if (p.B("WorkOffline") == true)
        {
            states.Add(Pair("脱机", "Offline"));
        }

        if (p.B("PrinterStatus") is null && p.I32("PrinterStatus") is { } code)
        {
            states.Add(code switch
            {
                1 => Pair("其他", "Other"),
                2 => Fmt.Unknown,
                3 => Pair("空闲", "Idle"),
                4 => Pair("正在打印", "Printing"),
                5 => Pair("预热中", "Warming up"),
                6 => Pair("已停止打印", "Stopped printing"),
                7 => Pair("脱机", "Offline"),
                _ => "State " + code,
            });
        }

        return states.Count == 0 ? null : string.Join(" · ", states);
    }

    // ================================================================== security
    public static Section Security()
    {
        var s = new Section("security", "nav.security", "\uE72E");

        var tpmGroup = s.AddGroup("grp.security");
        var tpm = Wmi.First("SELECT * FROM Win32_Tpm", TpmNs);
        tpmGroup
            .Add("f.tpmPresent", Fmt.B(tpm is not null))
            .AddIf("f.tpmSpec", tpm?.StrOrNull("SpecVersion"))
            .AddIf("f.tpmMfr", tpm?.StrOrNull("ManufacturerIdTxt"))
            .AddIf("f.tpmVersion", tpm?.StrOrNull("ManufacturerVersion"))
            .AddIf("f.tpmEnabled", tpm?.B("IsEnabled_InitialValue") is { } en ? Fmt.B(en) : null)
            .AddIf("f.tpmActivated", tpm?.B("IsActivated_InitialValue") is { } act ? Fmt.B(act) : null)
            .AddIf("f.tpmOwned", tpm?.B("IsOwned_InitialValue") is { } own ? Fmt.B(own) : null)
            .Add("f.secureBootState", SecureBootDetailed(), tone: Tone.Normal);

        var defender = s.AddGroup("grp.security");
        defender.Items.Clear();
        var av = Wmi.First("SELECT * FROM AntiVirusProduct", SecurityCenterNs);
        var state = av?.StrOrNull("productState");
        var (enabled, upToDate) = ParseAvState(state);
        defender
            .AddIf("f.defenderAntivirus", av is null ? Pair("未检测到已注册的杀毒软件", "No registered antivirus") : Fmt.B(enabled))
            .AddIf("f.defenderRealtime", av is null ? null : Fmt.B(upToDate))
            .AddIf("f.defenderSig", Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Signature Updates", "AVSignatureVersion", view64: true))
            .AddIf("f.defenderEngine", Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Signature Updates", "AMEngineVersion", view64: true))
            .AddIf("f.defenderSigAge", Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows Defender\Signature Updates", "SignaturesLastUpdated", view64: true) is { } t
                && long.TryParse(t, out var epoch) && epoch > 0
                ? DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : null)
            .AddIf("f.defenderThreats", ThreatHistory())
            .AddIf("f.firewallProfiles", FirewallState());

        var features = s.AddGroup("grp.security");
        features.Items.Clear();
        var smartScreen = Reg.Dword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled") is { } ss
            ? (ss != 0 ? Fmt.Yes : Fmt.No)
            : Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen");
        features
            .AddIf("f.uac", UacLevel())
            .AddIf("f.smartscreen", smartScreen)
            .AddIf("f.vbs", VbsStatus())
            .AddIf("f.hyperV", Reg.Dword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Virtualization", "HyperVRequirementVirtualizationFirmwareEnabled") is null
                ? OptionalFeature("Microsoft-Hyper-V")
                : Fmt.Yes)
            .AddIf("f.sandbox", OptionalFeature("Containers-DisposableClientVM"))
            .AddIf("f.wsl", OptionalFeature("Microsoft-Windows-Subsystem-Linux"));

        var bitlocker = s.AddGroup("grp.security");
        bitlocker.Items.Clear();
        var volumes = Wmi.Query("SELECT * FROM Win32_EncryptableVolume", BitLockerNs);
        if (volumes.Count == 0)
        {
            bitlocker.Add("f.bitlocker", Pair("不可用（需要管理员权限或未启用）", "Unavailable (needs elevation or not enabled)"), tone: Tone.Muted);
        }
        else
        {
            foreach (var v in volumes)
            {
                var letter = v.StrOrNull("DriveLetter") ?? "?";
                var protection = v.I32("ProtectionStatus");
                bitlocker.Add(
                    "f.bitlockerProtection",
                    letter + "  " + (protection switch
                    {
                        0 => Pair("未加密", "Off"),
                        1 => Pair("已加密", "On"),
                        2 => Pair("未知", "Unknown"),
                        _ => Fmt.Unknown,
                    }),
                    tone: protection == 1 ? Tone.Good : Tone.Muted);
            }
        }

        s.Summary = string.Join(" · ", new[]
        {
            tpm is null ? Pair("无 TPM", "No TPM") : "TPM " + (tpm.StrOrNull("SpecVersion")?.Split(',')[0].Trim() ?? string.Empty),
            SecureBootDetailed(),
            av is null ? null : Pair("杀毒软件已注册", "AV registered"),
        }.Where(Fmt.Has));
        return s;
    }

    private static string SecureBootDetailed()
    {
        var v = Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        return v switch
        {
            0 => Pair("已关闭", "Disabled"),
            1 => Pair("已启用", "Enabled"),
            _ => Fmt.Unknown,
        };
    }

    private static (bool? Enabled, bool? UpToDate) ParseAvState(string? productState)
    {
        if (!Fmt.Has(productState) || !ushort.TryParse(productState, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var state))
        {
            return (null, null);
        }

        // SecurityCenter2 productState: 0x10/0x11 flags (enabled, up-to-date) in the high byte group
        var enabled = (state & 0x1000) != 0;
        var upToDate = (state & 0x10) != 0;
        return (enabled, upToDate);
    }

    private static string? ThreatHistory()
    {
        var threats = Wmi.Query("SELECT * FROM MSFT_MpThreat", @"\\.\root\Microsoft\Windows\Defender");
        if (threats.Count == 0)
        {
            return Pair("无记录", "None");
        }

        return threats.Count.ToString(CultureInfo.CurrentCulture) + Pair(" 条记录", " entries");
    }

    private static string? UacLevel()
    {
        var v = Reg.Dword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
        if (v is null)
        {
            return null;
        }

        var consent = Reg.Dword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "ConsentPromptBehaviorAdmin");
        var level = consent switch
        {
            0 => Pair("从不通知", "Never notify"),
            1 => Pair("询问凭据（安全桌面）", "Prompt for credentials on secure desktop"),
            2 => Pair("询问同意（安全桌面）", "Prompt for consent on secure desktop"),
            3 => Pair("询问凭据", "Prompt for credentials"),
            4 => Pair("询问同意", "Prompt for consent"),
            5 => Pair("仅提示非 Windows 程序", "Prompt for consent for non-Windows binaries"),
            _ => null,
        };

        return v.Value == 0
            ? Pair("已关闭", "Disabled")
            : (level is null ? Pair("已启用", "Enabled") : Pair("已启用", "Enabled") + " · " + level);
    }

    private static string? VbsStatus()
    {
        var running = Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "EnableVirtualizationBasedSecurity");
        if (running is null)
        {
            return null;
        }

        if (running.Value == 0)
        {
            return Pair("未启用", "Not enabled");
        }

        var status = Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard", "VirtualizationBasedSecurityStatus");
        return status switch
        {
            0 => Pair("未启用", "Not enabled"),
            1 => Pair("已启用但未运行", "Enabled but not running"),
            2 => Pair("正在运行", "Running"),
            _ => Pair("已启用", "Enabled"),
        };
    }

    private static string? OptionalFeature(string featureName)
    {
        try
        {
            var row = Wmi.First("SELECT * FROM Win32_OptionalFeature WHERE Name='" + featureName.Replace("'", "''") + "'");
            if (row is null)
            {
                return null;
            }

            return row.I32("InstallState") switch
            {
                1 => Pair("已启用", "Enabled"),
                2 => Pair("已禁用", "Disabled"),
                3 => Pair("已安装", "Installed"),
                4 => Pair("未安装", "Not installed"),
                _ => Fmt.Unknown,
            };
        }
        catch
        {
            return null;
        }
    }

    // ================================================================== sensors
    public static Section Sensors()
    {
        var s = new Section("sensors", "nav.sensors", "\uE9D9");
        var zones = Wmi.Query("SELECT * FROM MSAcpi_ThermalZoneTemperature");
        var fans = Wmi.Query("SELECT * FROM Win32_Fan");
        var processors = Wmi.Query("SELECT * FROM Win32_Processor");
        var battery = Wmi.Query("SELECT * FROM Win32_Battery");
        var found = false;

        if (zones.Count > 0)
        {
            var g = s.AddGroup("grp.thermal");
            foreach (var z in zones)
            {
                var raw = z.D("CurrentTemperature");
                var celsius = raw is > 0 ? (raw.Value / 10.0) - 273.15 : (double?)null;
                var name = z.StrOrNull("InstanceName") ?? Pair("热区域", "Thermal zone");
                g.Add(ShortZone(name), Lookup.ThermalZoneTemperature(raw), tone: Lookup.TemperatureTone(celsius));
                found = true;
            }
        }

        var counters = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation");
        if (counters.Count > 0)
        {
            var g = s.AddGroup("grp.thermal");
            foreach (var row in counters.Take(12))
            {
                var kelvin = row.D("Temperature");
                var celsius = kelvin is > 200 ? kelvin.Value - 273.15 : (double?)null;
                g.Add(
                    row.StrOrNull("Name") ?? Pair("温度传感器", "Temperature sensor"),
                    kelvin is null ? Fmt.Unknown : Lookup.ThermalZoneTemperature(kelvin * 10),
                    tone: Lookup.TemperatureTone(celsius));
                found = true;
            }
        }

        if (fans.Count > 0)
        {
            var g = s.AddGroup("grp.fan");
            foreach (var f in fans)
            {
                g.Add(
                    f.StrOrNull("DeviceID") ?? f.StrOrNull("Name") ?? Pair("风扇", "Fan"),
                    f.U32("DesiredSpeed") is { } rpm ? rpm.ToString("N0", CultureInfo.CurrentCulture) + " RPM" : Fmt.Unknown,
                    tone: Tone.Normal);
                found = true;
            }
        }

        if (battery.Count > 0)
        {
            var g = s.AddGroup("grp.sensorOther");
            foreach (var b in battery)
            {
                g.AddIf("f.batVoltage", b.U32("DesignVoltage") is { } mv and > 0 ? (mv / 1000.0).ToString("N2", CultureInfo.CurrentCulture) + " V" : null);
                g.AddIf("f.batChemistry", Lookup.BatteryChemistry(b.I32("Chemistry")));
                found = true;
            }
        }

        if (!found)
        {
            var note = s.AddGroup("grp.sensorOther");
            note.Add("f.sensorNoData", Babel.L["f.sensorNoData"], tone: Tone.Muted);
            s.Severity = Condition.Info;
        }

        s.Summary = found ? Pair("已读取传感器数据", "Sensor data available") : Pair("无传感器数据", "No sensor data");
        return s;
    }

    private static string ShortZone(string instanceName)
    {
        var idx = instanceName.LastIndexOf('\\');
        return idx >= 0 && idx < instanceName.Length - 1 ? instanceName[(idx + 1)..] : instanceName;
    }

    // ================================================================== helpers
    private static string? DriverVersionOf(string? deviceId)
    {
        if (!Fmt.Has(deviceId))
        {
            return null;
        }

        var row = Wmi.First("SELECT * FROM Win32_PnPSignedDriver WHERE DeviceID='" + deviceId!.Replace("'", "''") + "'");
        return row?.StrOrNull("DriverVersion");
    }
}
