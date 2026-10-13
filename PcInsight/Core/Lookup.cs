using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PcInsight.Core;

/// <summary>Lookup tables for the numeric WMI enumerations we display.</summary>
public static class Lookup
{
    private static bool Zh => Babel.L.IsChinese;

    private static string Pair(string zh, string en) => Zh ? zh : en;

    public static string MemoryType(int? code) => code switch
    {
        0 => Fmt.Unknown,
        1 => "Other",
        2 => "DRAM",
        3 => "Synchronous DRAM",
        4 => "Cache DRAM",
        5 => "EDO",
        6 => "EDRAM",
        7 => "VRAM",
        8 => "SRAM",
        9 => "RAM",
        10 => "ROM",
        11 => "Flash",
        12 => "EEPROM",
        13 => "FEPROM",
        14 => "EPROM",
        15 => "CDRAM",
        16 => "3DRAM",
        17 => "SDRAM",
        18 => "SGRAM",
        19 => "RDRAM",
        20 => "DDR",
        21 => "DDR2",
        22 => "DDR2 FB-DIMM",
        24 => "DDR3",
        25 => "FBD2",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        31 => "Logical non-volatile device",
        32 => "HBM",
        33 => "HBM2",
        34 => "DDR5",
        35 => "LPDDR5",
        36 => "HBM3",
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string MemoryFormFactor(int? code) => code switch
    {
        0 => Fmt.Unknown,
        1 => "Other",
        2 => "SIP",
        3 => "DIP",
        4 => "ZIP",
        5 => "SOJ",
        6 => "Proprietary",
        7 => "SIMM",
        8 => "DIMM",
        9 => "TSOP",
        10 => "PGA",
        11 => "RIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        14 => "SMD",
        15 => "SSMP",
        16 => "QFP",
        17 => "TQFP",
        18 => "SOIC",
        19 => "LCC",
        20 => "PLCC",
        21 => "BGA",
        22 => "FPBGA",
        23 => "LGA",
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string MemoryArrayType(int? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "SIMM",
        4 => "SIP",
        5 => "Chip",
        6 => "DIP",
        7 => "ZIP",
        8 => "Proprietary DIMM",
        9 => "DIMM",
        10 => "TSOP",
        11 => "Row of chips",
        12 => "RIMM",
        13 => "SODIMM",
        14 => "SRIMM",
        15 => "FB-DIMM",
        16 => "Die",
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string PhysicalMemoryArrayType(int? code) => MemoryArrayType(code);

    public static string ChassisType(int? code) => code switch
    {
        1 => Pair("其他", "Other"),
        2 => Pair("未知", "Unknown"),
        3 => Pair("台式机", "Desktop"),
        4 => Pair("低流线型台式机", "Low-profile desktop"),
        5 => Pair("披萨盒", "Pizza box"),
        6 => Pair("小型机箱", "Mini tower"),
        7 => Pair("塔式机箱", "Tower"),
        8 => Pair("便携式", "Portable"),
        9 => Pair("笔记本", "Laptop"),
        10 => Pair("笔记本", "Notebook"),
        11 => Pair("手持设备", "Hand held"),
        12 => Pair("扩展坞", "Docking station"),
        13 => Pair("一体机", "All-in-one"),
        14 => Pair("亚笔记本", "Sub notebook"),
        15 => Pair("空间节省型", "Space-saving"),
        16 => Pair("午餐盒", "Lunch box"),
        17 => Pair("大型机箱", "Main system chassis"),
        18 => Pair("扩展机箱", "Expansion chassis"),
        19 => Pair("机架式", "Sub-chassis"),
        20 => Pair("刀片式", "Blade"),
        21 => Pair("刀片机箱", "Blade enclosure"),
        22 => Pair("平板电脑", "Tablet"),
        23 => Pair("可转换式", "Convertible"),
        24 => Pair("可拆卸式", "Detachable"),
        25 => Pair("IoT 网关", "IoT gateway"),
        26 => Pair("嵌入式 PC", "Embedded PC"),
        27 => Pair("迷你 PC", "Mini PC"),
        28 => Pair("棒状 PC", "Stick PC"),
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string PcSystemType(int? code) => code switch
    {
        0 => Pair("台式机", "Desktop"),
        1 => Pair("移动/笔记本", "Mobile / laptop"),
        2 => Pair("工作站", "Workstation"),
        3 => Pair("服务器", "Server"),
        4 => Pair("入门服务器", "Entry-level server"),
        5 => Pair("小型服务器", "Small server"),
        6 => Pair("个人服务器", "Personal server"),
        7 => Pair("小型办公服务器", "SOHO server"),
        8 => Pair("移动服务器", "Mobile server"),
        9 => Pair("性能服务器", "Performance server"),
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string Availability(int? code) => code switch
    {
        1 => Pair("其他", "Other"),
        2 => Pair("未知", "Unknown"),
        3 => Pair("正在运行/满负荷", "Running / full power"),
        4 => Pair("警告", "Warning"),
        5 => Pair("测试中", "In test"),
        6 => Pair("不适用", "Not applicable"),
        7 => Pair("电源关闭", "Power off"),
        8 => Pair("离线", "Off line"),
        9 => Pair("脱机", "Off duty"),
        10 => Pair("性能下降", "Degraded"),
        11 => Pair("未安装", "Not installed"),
        12 => Pair("安装错误", "Install error"),
        13 => Pair("省电模式", "Power save - unknown"),
        14 => Pair("省电-低功耗", "Power save - low power mode"),
        15 => Pair("省电-待机", "Power save - standby"),
        16 => Pair("电源循环中", "Power cycle"),
        17 => Pair("省电-警告", "Power save - warning"),
        18 => Pair("暂停", "Paused"),
        19 => Pair("未就绪", "Not ready"),
        20 => Pair("未配置", "Not configured"),
        21 => Pair("静止", "Quiesced"),
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static Tone AvailabilityTone(int? code) => code switch
    {
        3 => Tone.Good,
        10 or 4 or 17 or 19 or 20 => Tone.Warn,
        11 or 12 or 8 => Tone.Bad,
        _ => Tone.Normal,
    };

    public static string NetConnectionStatus(int? code, bool up)
    {
        if (up)
        {
            return Pair("已连接", "Connected");
        }

        return code switch
        {
            0 => Pair("已断开", "Disconnected"),
            1 => Pair("正在连接", "Connecting"),
            2 => Pair("已连接", "Connected"),
            3 => Pair("正在断开", "Disconnecting"),
            4 => Pair("硬件不存在", "Hardware not present"),
            5 => Pair("硬件已禁用", "Hardware disabled"),
            6 => Pair("硬件故障", "Hardware malfunction"),
            7 => Pair("媒体已断开", "Media disconnected"),
            8 => Pair("正在验证身份", "Authenticating"),
            9 => Pair("身份验证成功", "Authentication succeeded"),
            10 => Pair("身份验证失败", "Authentication failed"),
            11 => Pair("地址无效", "Invalid address"),
            12 => Pair("凭据不足", "Credentials required"),
            _ => Pair("未连接", "Not connected"),
        };
    }

    public static string NetType(int? code) => code switch
    {
        0 => Pair("以太网", "Ethernet 802.3"),
        1 => Pair("令牌环", "Token Ring 802.5"),
        2 => Pair("FDDI", "FDDI"),
        3 => Pair("WAN", "WAN"),
        4 => Pair("本地环回", "LocalTalk"),
        5 => Pair("以太网", "Ethernet"),
        6 => Pair("无线", "Wireless"),
        7 => Pair("无线 (802.11)", "Wireless 802.11"),
        9 => Pair("无线 WAN", "Wireless WAN"),
        10 => Pair("无线 (蓝牙)", "Bluetooth"),
        11 => Pair("无线 (Wi-Fi)", "Wi-Fi"),
        _ => code is null ? Fmt.Unknown : Pair("类型 " + code, "Type " + code),
    };

    public static string DriveMediaType(int? code) => code switch
    {
        0 => Pair("未知", "Unknown"),
        3 => Pair("硬盘 (HDD)", "Hard disk drive"),
        4 => Pair("可移动磁盘", "Removable media"),
        5 => Pair("软盘", "Floppy disk"),
        _ => code is null ? Fmt.Unknown : Pair("类型 " + code, "Type " + code),
    };

    public static string BusType(string? msftBusType)
    {
        if (!int.TryParse(msftBusType, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return Fmt.Unknown;
        }

        return code switch
        {
            1 => "SCSI",
            2 => "ATAPI",
            3 => "ATA",
            4 => "IEEE 1394",
            5 => "SSA",
            6 => "Fibre Channel",
            7 => "USB",
            8 => "RAID",
            9 => "iSCSI",
            10 => "SAS",
            11 => "SATA",
            12 => "SD",
            13 => "MMC",
            14 => "Virtual",
            15 => "File-backed virtual",
            16 => "Storage Spaces",
            17 => "NVMe",
            18 => "SCM",
            19 => "UFS",
            _ => "Bus " + code,
        };
    }

    public static string BatteryChemistry(int? code) => code switch
    {
        1 => Pair("其他", "Other"),
        2 => Pair("未知", "Unknown"),
        3 => Pair("铅酸", "Lead acid"),
        4 => Pair("镍镉", "Nickel cadmium"),
        5 => Pair("镍氢", "Nickel metal hydride"),
        6 => Pair("锂离子", "Lithium ion"),
        7 => Pair("锌空", "Zinc air"),
        8 => Pair("锂聚合物", "Lithium polymer"),
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string BatteryStatus(int? code) => code switch
    {
        1 => Pair("放电中", "Discharging"),
        2 => Pair("交流供电", "On AC power"),
        3 => Pair("已充满", "Fully charged"),
        4 => Pair("低电量", "Low"),
        5 => Pair("临界", "Critical"),
        6 => Pair("充电中", "Charging"),
        7 => Pair("充电且电量高", "Charging and high"),
        8 => Pair("充电且电量低", "Charging and low"),
        9 => Pair("充电且临界", "Charging and critical"),
        10 => Pair("未定义", "Undefined"),
        11 => Pair("部分充电", "Partially charged"),
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    public static string ServiceStartMode(string? mode) => mode switch
    {
        "Auto" => Pair("自动", "Automatic"),
        "Manual" => Pair("手动", "Manual"),
        "Disabled" => Pair("已禁用", "Disabled"),
        "Boot" => Pair("引导", "Boot"),
        "System" => Pair("系统", "System"),
        null or "" => Fmt.Unknown,
        _ => mode!,
    };

    public static string ServiceState(string? state) => state switch
    {
        "Running" => Pair("正在运行", "Running"),
        "Stopped" => Pair("已停止", "Stopped"),
        "Paused" => Pair("已暂停", "Paused"),
        "Start Pending" => Pair("正在启动", "Starting"),
        "Stop Pending" => Pair("正在停止", "Stopping"),
        "Continue Pending" => Pair("正在继续", "Continuing"),
        "Pause Pending" => Pair("正在暂停", "Pausing"),
        "Unknown" => Fmt.Unknown,
        null or "" => Fmt.Unknown,
        _ => state!,
    };

    public static string ServiceType(string? type)
    {
        if (string.IsNullOrEmpty(type))
        {
            return Fmt.Unknown;
        }

        var sb = new List<string>();
        if (type!.Contains("Kernel Driver", StringComparison.OrdinalIgnoreCase))
        {
            sb.Add(Pair("内核驱动", "Kernel driver"));
        }
        else if (type.Contains("File System Driver", StringComparison.OrdinalIgnoreCase))
        {
            sb.Add(Pair("文件系统驱动", "File system driver"));
        }
        else if (type.Contains("Recognizer Driver", StringComparison.OrdinalIgnoreCase))
        {
            sb.Add(Pair("识别器驱动", "Recognizer driver"));
        }
        else
        {
            if (type.Contains("Own Process", StringComparison.OrdinalIgnoreCase))
            {
                sb.Add(Pair("独立进程", "Own process"));
            }

            if (type.Contains("Share Process", StringComparison.OrdinalIgnoreCase))
            {
                sb.Add(Pair("共享进程", "Shared process"));
            }

            if (type.Contains("Interactive", StringComparison.OrdinalIgnoreCase))
            {
                sb.Add(Pair("可交互", "Interactive"));
            }
        }

        return sb.Count > 0 ? string.Join(" / ", sb) : type;
    }

    public static string ThermalState(int? code)
    {
        if (code is null)
        {
            return Fmt.Unknown;
        }

        return code.Value switch
        {
            0 => Pair("安全", "Safe"),
            1 => Pair("警告", "Warning"),
            2 => Pair("临界", "Critical"),
            3 => Pair("不可恢复", "Unrecoverable"),
            4 => Pair("未知", "Unknown"),
            _ => "0x" + code.Value.ToString("X"),
        };
    }

    public static string ProcessorFamily(int? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        11 => "Pentium brand",
        12 => "Pentium Pro",
        13 => "Pentium II",
        14 => "Pentium MMX",
        28 => "AMD Athlon",
        29 => "AMD Duron",
        30 => "AMD K5",
        31 => "AMD K6",
        32 => "AMD K6-2",
        33 => "AMD K6-III",
        34 => "AMD Athlon XP",
        35 => "AMD Athlon MP",
        36 => "AMD Sempron",
        84 => "Intel Core 2 Duo",
        85 => "Intel Core 2 Solo",
        86 => "Intel Core 2 Extreme",
        87 => "Intel Core 2 Quad",
        88 => "Intel Core 2 Extreme Mobile",
        89 => "Intel Core 2 Duo Mobile",
        90 => "Intel Core 2 Solo Mobile",
        91 => "Intel Core i7",
        100 => "AMD Athlon II",
        101 => "AMD Opteron 6100",
        102 => "AMD Opteron 4100",
        103 => "AMD Opteron 6200",
        104 => "AMD Opteron 4200",
        105 => "AMD FX",
        107 => "AMD C-Series",
        108 => "AMD E-Series",
        109 => "AMD A-Series",
        110 => "AMD G-Series",
        111 => "AMD Z-Series",
        112 => "AMD R-Series",
        113 => "AMD Opteron 4300",
        115 => "AMD Opteron 3300",
        116 => "AMD FirePro",
        198 => "Intel Core i7",
        199 => "Intel Core i5",
        200 => "Intel Core i3",
        201 => "Intel Core i9",
        _ => code is null ? Fmt.Unknown : "Family " + code,
    };

    public static string ProcessorType(int? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => Pair("中央处理器", "Central processor"),
        4 => Pair("数学协处理器", "Math processor"),
        5 => Pair("DSP 处理器", "DSP processor"),
        6 => Pair("视频处理器", "Video processor"),
        _ => code is null ? Fmt.Unknown : "Type " + code,
    };

    public static string UpgradeMethod(int? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "Daughter board",
        4 => "ZIF socket",
        5 => "Replacement/piggy back",
        6 => "None",
        7 => "LIF socket",
        8 => "Slot 1",
        9 => "Slot 2",
        10 => "370-pin socket",
        11 => "Slot A",
        12 => "Slot M",
        13 => "Socket 423",
        14 => "Socket A (Socket 462)",
        15 => "Socket 478",
        16 => "Socket 754",
        17 => "Socket 940",
        18 => "Socket 939",
        19 => "Socket mPGA604",
        20 => "Socket LGA771",
        21 => "Socket LGA775",
        22 => "Socket S1",
        23 => "Socket AM2",
        24 => "Socket F (1207)",
        25 => "Socket LGA1366",
        26 => "Socket G34",
        27 => "Socket AM3",
        28 => "Socket C32",
        29 => "Socket LGA1156",
        30 => "Socket LGA1567",
        31 => "Socket PGA988A",
        32 => "Socket BGA1288",
        33 => "Socket rPGA988B",
        34 => "Socket BGA1023",
        35 => "Socket BGA1224",
        36 => "Socket LGA1155",
        37 => "Socket LGA1356",
        38 => "Socket LGA2011",
        39 => "Socket FS1",
        40 => "Socket FS2",
        41 => "Socket FM1",
        42 => "Socket FM2",
        43 => "Socket LGA2011-3",
        44 => "Socket LGA1356-3",
        45 => "Socket LGA1150",
        46 => "Socket BGA1168",
        47 => "Socket BGA1234",
        48 => "Socket BGA1364",
        49 => "Socket AM4",
        50 => "Socket LGA1151",
        51 => "Socket BGA1356",
        52 => "Socket BGA1440",
        53 => "Socket BGA1515",
        54 => "Socket LGA3647-1",
        55 => "Socket SP3",
        56 => "Socket SP3r2",
        57 => "Socket LGA2066",
        58 => "Socket BGA1392",
        59 => "Socket BGA1510",
        60 => "Socket BGA1528",
        61 => "Socket LGA4189",
        62 => "Socket LGA1200",
        63 => "Socket LGA4677",
        64 => "Socket LGA1700",
        65 => "Socket BGA1744",
        66 => "Socket BGA1781",
        67 => "Socket BGA1211",
        68 => "Socket BGA2422",
        69 => "Socket LGA1211",
        70 => "Socket LGA2422",
        71 => "Socket LGA5773",
        72 => "Socket BGA5773",
        73 => "Socket AM5",
        74 => "Socket SP5",
        75 => "Socket SP6",
        76 => "Socket BGA883",
        77 => "Socket BGA1190",
        78 => "Socket BGA4129",
        79 => "Socket LGA4710",
        80 => "Socket LGA7529",
        _ => code is null ? Fmt.Unknown : "Socket/Upgrade " + code,
    };

    public static string VideoArchitecture(int? code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => "VGA",
        4 => "SVGA",
        5 => "MDA",
        6 => "HGC",
        7 => "CGA",
        8 => "EGA",
        9 => "EGA/VGA",
        10 => "PGA",
        11 => "VGA",
        12 => "XGA",
        13 => "SXGA",
        14 => "WXGA",
        15 => "UVGA",
        16 => "SXGA+",
        160 => "WXGA+",
        161 => "WSXGA+",
        162 => "WUXGA",
        163 => "WQXGA",
        164 => "WQHD",
        165 => "WQXGA+",
        166 => "UHD",
        _ => code is null ? Fmt.Unknown : "0x" + code.Value.ToString("X"),
    };

    /// <summary>Format a bits-per-second link speed (WMI reports bps as a string).</summary>
    public static string LinkSpeed(string? bps)
    {
        if (!ulong.TryParse(bps, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v == 0)
        {
            return Fmt.Unknown;
        }

        double bits = v;
        string[] units = { "bps", "Kbps", "Mbps", "Gbps", "Tbps" };
        var i = 0;
        while (bits >= 1000 && i < units.Length - 1)
        {
            bits /= 1000;
            i++;
        }

        return bits.ToString(bits >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[i];
    }

    public static string ErrorLevel(int? code) => code switch
    {
        0 => Pair("正常", "Healthy"),
        1 => Pair("警告", "Warning"),
        2 => Pair("错误", "Error"),
        3 => Pair("服务未知", "Service unknown"),
        4 => Pair("服务未安装", "Service not installed"),
        5 => Pair("驱动未知", "Driver unknown"),
        6 => Pair("驱动未安装", "Driver not installed"),
        7 => Pair("驱动已禁用", "Driver disabled"),
        8 => Pair("驱动不可用", "Driver unavailable"),
        9 => Pair("驱动无响应", "Driver unresponsive"),
        10 => Pair("已降级", "Degraded"),
        11 => Pair("未知", "Unknown"),
        _ => code is null ? Fmt.Unknown : "Error " + code,
    };

    public static Tone ErrorLevelTone(int? code) => code switch
    {
        0 or 3 or 4 or 11 => code == 0 ? Tone.Good : Tone.Normal,
        1 or 10 => Tone.Warn,
        2 or 5 or 6 or 8 or 9 => Tone.Bad,
        7 => Tone.Muted,
        _ => Tone.Normal,
    };

    public static string ThermalZoneTemperature(double? kelvinTenths)
    {
        // MSAcpi_ThermalZoneTemperature reports tenths of a kelvin.
        if (kelvinTenths is null or <= 0 or > 5000)
        {
            return Fmt.Unknown;
        }

        var celsius = (kelvinTenths.Value / 10.0) - 273.15;
        if (celsius is < -50 or > 250)
        {
            return Fmt.Unknown;
        }

        return celsius.ToString("N1", CultureInfo.CurrentCulture) + " °C";
    }

    public static Tone TemperatureTone(double? celsius)
    {
        if (celsius is null)
        {
            return Tone.Muted;
        }

        return celsius.Value switch
        {
            < 60 => Tone.Good,
            < 80 => Tone.Normal,
            < 90 => Tone.Warn,
            _ => Tone.Bad,
        };
    }

    public static string WifiStandard(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return Fmt.Unknown;
        }

        var d = description!;
        if (d.Contains("BE", StringComparison.OrdinalIgnoreCase) || d.Contains("Wi-Fi 7", StringComparison.OrdinalIgnoreCase))
        {
            return "Wi-Fi 7 (802.11be)";
        }

        if (d.Contains("AX", StringComparison.OrdinalIgnoreCase) || d.Contains("Wi-Fi 6", StringComparison.OrdinalIgnoreCase))
        {
            return "Wi-Fi 6 / 6E (802.11ax)";
        }

        if (d.Contains("AC", StringComparison.OrdinalIgnoreCase))
        {
            return "Wi-Fi 5 (802.11ac)";
        }

        if (d.Contains("Wireless-N", StringComparison.OrdinalIgnoreCase) || d.Contains("N 2230", StringComparison.OrdinalIgnoreCase) || d.Contains("N 7260", StringComparison.OrdinalIgnoreCase))
        {
            return "Wi-Fi 4 (802.11n)";
        }

        return d;
    }
}
