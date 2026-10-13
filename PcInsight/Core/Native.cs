using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PcInsight.Core;

/// <summary>
/// Native helpers: system times, memory status, power status, display topology, DPI and the
/// monitor EDID block. Everything is read-only.
/// </summary>
public static class Native
{
    // ---------------------------------------------------------------- sysinfo
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;

        public ulong ToUlong() => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("kernel32.dll")]
    private static extern void GetNativeSystemInfo(out SYSTEM_INFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_INFO
    {
        public ushort ProcessorArchitecture;
        public ushort Reserved;
        public uint PageSize;
        public IntPtr MinimumApplicationAddress;
        public IntPtr MaximumApplicationAddress;
        public IntPtr ActiveProcessorMask;
        public uint NumberOfProcessors;
        public uint ProcessorType;
        public uint AllocationGranularity;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    /// <summary>CPU load sampled between two GetSystemTimes reads (more accurate than WMI for a snapshot).</summary>
    public static double? CpuLoadSample(int milliseconds = 350)
    {
        try
        {
            if (!GetSystemTimes(out var idle1, out var kernel1, out var user1))
            {
                return null;
            }

            System.Threading.Thread.Sleep(milliseconds);

            if (!GetSystemTimes(out var idle2, out var kernel2, out var user2))
            {
                return null;
            }

            var idle = idle2.ToUlong() - idle1.ToUlong();
            var kernel = kernel2.ToUlong() - kernel1.ToUlong();
            var user = user2.ToUlong() - user1.ToUlong();
            var total = kernel + user;
            if (total == 0)
            {
                return null;
            }

            return Math.Round(100.0 * (total - idle) / total, 1);
        }
        catch
        {
            return null;
        }
    }

    public static ulong UptimeSeconds() => GetTickCount64() / 1000UL;

    public static DateTime? LastBootTime()
    {
        var uptime = UptimeSeconds();
        return uptime == 0 ? null : DateTime.Now - TimeSpan.FromSeconds(uptime);
    }

    public enum Firmware
    {
        Unknown,
        Bios,
        Uefi,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    /// <summary>FirmwareTypeBios = 1, FirmwareTypeUefi = 2.</summary>
    public static Firmware FirmwareType()
    {
        try
        {
            if (GetFirmwareType(out var type))
            {
                return type switch
                {
                    1 => Firmware.Bios,
                    2 => Firmware.Uefi,
                    _ => Firmware.Unknown,
                };
            }
        }
        catch
        {
            // not available on this build
        }

        return Firmware.Unknown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    public sealed record MemoryStatus(ulong TotalPhysical, ulong AvailablePhysical, ulong TotalPageFile, ulong AvailablePageFile, uint LoadPercent)
    {
        public ulong UsedPhysical => TotalPhysical - AvailablePhysical;

        public ulong UsedPageFile => TotalPageFile - AvailablePageFile;
    }

    public static MemoryStatus? Memory()
    {
        try
        {
            var m = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m))
            {
                return null;
            }

            return new MemoryStatus(m.TotalPhys, m.AvailPhys, m.TotalPageFile, m.AvailPageFile, m.MemoryLoad);
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- power
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    public sealed record PowerStatus(bool? AcOnline, int? BatteryPercent, TimeSpan? Remaining, bool Charging, bool NoBattery);

    public static PowerStatus? Power()
    {
        try
        {
            if (!GetSystemPowerStatus(out var s))
            {
                return null;
            }

            bool? ac = s.ACLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null,
            };
            int? pct = s.BatteryLifePercent == 255 ? null : s.BatteryLifePercent;
            var noBattery = s.BatteryFlag == 128 || s.BatteryLifePercent == 255;
            TimeSpan? remaining = s.BatteryLifeTime == uint.MaxValue || s.BatteryLifeTime == 0
                ? null
                : TimeSpan.FromSeconds(s.BatteryLifeTime);
            return new PowerStatus(ac, pct, remaining, (s.BatteryFlag & 8) != 0, noBattery);
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- process architecture
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    public static (string Process, string Native) ProcessArchitecture()
    {
        try
        {
            if (IsWow64Process2(GetCurrentProcess(), out var proc, out var native))
            {
                // IMAGE_FILE_MACHINE_UNKNOWN (0) means "not emulated": the process runs as the
                // native architecture.
                var processName = proc == 0 ? MachineName(native) : MachineName(proc);
                return (processName, MachineName(native));
            }
        }
        catch
        {
            // older OS or unexpected failure — fall back below
        }

        GetNativeSystemInfo(out var info);
        return (RuntimeInformation.ProcessArchitecture.ToString(), MachineName(info.ProcessorArchitecture));
    }

    private static string MachineName(ushort machine) => machine switch
    {
        0 => "Unknown",
        0x014c => "x86",
        0x8664 => "x64",
        0xAA64 => "ARM64",
        0x01c4 => "ARM",
        0x0200 => "IA64",
        _ => "0x" + machine.ToString("X4"),
    };

    // ---------------------------------------------------------------- displays
    public sealed class MonitorInfo
    {
        public string DeviceName { get; set; } = string.Empty;

        public string DeviceId { get; set; } = string.Empty;

        public bool Primary { get; set; }

        public int Left { get; set; }

        public int Top { get; set; }

        public int Right { get; set; }

        public int Bottom { get; set; }

        public uint Dpi { get; set; } = 96;

        public int RefreshHz { get; set; }

        public int BitsPerPixel { get; set; }

        public int Width => Right - Left;

        public int Height => Bottom - Top;

        public int ScalePercent => (int)Math.Round(Dpi / 96.0 * 100);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public uint StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? device, uint devNum, ref DISPLAY_DEVICE info, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
            {
                var mi = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(h, ref mi))
                {
                    return true;
                }

                var info = new MonitorInfo
                {
                    DeviceName = mi.Device.Trim(),
                    Primary = (mi.Flags & 1) != 0,
                    Left = mi.Monitor.Left,
                    Top = mi.Monitor.Top,
                    Right = mi.Monitor.Right,
                    Bottom = mi.Monitor.Bottom,
                };

                try
                {
                    if (GetDpiForMonitor(h, 0, out var dx, out _) == 0 && dx > 0)
                    {
                        info.Dpi = dx;
                    }
                }
                catch
                {
                    // Shcore is unavailable on very old builds
                }

                list.Add(info);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // ignore
        }

        // enrich with device id + current mode
        for (uint i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0))
            {
                break;
            }

            if ((dd.StateFlags & 0x1) == 0)
            {
                continue; // not attached to the desktop
            }

            var match = list.Find(m => string.Equals(m.DeviceName, dd.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                continue;
            }

            match.DeviceId = dd.DeviceID.Trim();

            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettings(dd.DeviceName, -1, ref dm))
            {
                match.RefreshHz = (int)dm.dmDisplayFrequency;
                match.BitsPerPixel = (int)dm.dmBitsPerPel;
                if (match.Width == 0 && dm.dmPelsWidth > 0)
                {
                    match.Right = (int)dm.dmPelsWidth;
                    match.Bottom = (int)dm.dmPelsHeight;
                }
            }
        }

        if (list.Count == 0)
        {
            list.Add(new MonitorInfo { DeviceName = "DISPLAY1", Primary = true });
        }

        return list;
    }

    // ---------------------------------------------------------------- EDID
    public sealed record EdidInfo(string Manufacturer, int Year, int Week, double? DiagonalInches, string Version, bool Hdr)
    {
        public string SizeText => DiagonalInches is null
            ? string.Empty
            : DiagonalInches.Value.ToString("N1", System.Globalization.CultureInfo.CurrentCulture) + " \"";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint scope, uint hwProfile, uint keyType, uint samDesired);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegQueryValueEx(IntPtr key, string valueName, IntPtr reserved, out uint type, byte[] data, ref uint dataSize);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr key);

    /// <summary>Read the EDID block of every monitor-class device.</summary>
    public static List<EdidInfo> Edids()
    {
        var result = new List<EdidInfo>();
        var set = IntPtr.Zero;
        try
        {
            set = SetupDiGetClassDevs(IntPtr.Zero, "DISPLAY", IntPtr.Zero, 0x00000002 | 0x00000004);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
            {
                return result;
            }

            for (uint i = 0; ; i++)
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref data))
                {
                    break;
                }

                var key = SetupDiOpenDevRegKey(set, ref data, 1, 0, 1, 0x20019);
                if (key == IntPtr.Zero || key == new IntPtr(-1))
                {
                    continue;
                }

                try
                {
                    var buf = new byte[256];
                    uint size = (uint)buf.Length;
                    if (RegQueryValueEx(key, "EDID", IntPtr.Zero, out _, buf, ref size) == 0 && size >= 128)
                    {
                        var parsed = ParseEdid(buf, (int)size);
                        if (parsed is not null)
                        {
                            result.Add(parsed);
                        }
                    }
                }
                finally
                {
                    RegCloseKey(key);
                }
            }
        }
        catch
        {
            // EDID is a nice-to-have only
        }
        finally
        {
            if (set != IntPtr.Zero && set != new IntPtr(-1))
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        return result;
    }

    private static EdidInfo? ParseEdid(byte[] b, int length)
    {
        if (length < 128 || b[0] != 0x00 || b[1] != 0xFF || b[2] != 0xFF || b[3] != 0xFF)
        {
            return null;
        }

        var mfg = DecodeManufacturer((b[8] << 8) | b[9]);
        var week = b[16];
        var year = b[17] + 1990;
        var version = b[18] + "." + b[19];

        double? diagonal = null;
        if (b[21] > 0 && b[22] > 0)
        {
            double wcm = b[21];
            double hcm = b[22];
            diagonal = Math.Sqrt((wcm * wcm) + (hcm * hcm)) / 2.54;
        }

        var hdr = false;
        for (var off = 54; off + 18 <= 127; off += 18)
        {
            if (b[off] == 0x02 && b[off + 1] == 0x03 && b[off + 2] == 0x00 && (b[off + 7] & 0x01) != 0)
            {
                hdr = true;
            }
        }

        return new EdidInfo(mfg, year, week, diagonal, version, hdr);
    }

    private static string DecodeManufacturer(int code)
    {
        var chars = new char[3];
        chars[0] = (char)('A' + ((code >> 10) & 0x1F) - 1);
        chars[1] = (char)('A' + ((code >> 5) & 0x1F) - 1);
        chars[2] = (char)('A' + (code & 0x1F) - 1);
        var s = new string(chars);
        return s.All(char.IsLetter) ? s : "—";
    }

    // ---------------------------------------------------------------- misc
    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static uint ActiveProcessorCount()
    {
        try
        {
            return GetActiveProcessorCount(0xFFFF);
        }
        catch
        {
            return 0;
        }
    }
}

