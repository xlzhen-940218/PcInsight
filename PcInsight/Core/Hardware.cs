using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace PcInsight.Core;

/// <summary>
/// Hardware collectors — core sections (overview, processor, memory, storage).
/// Every method fills one <see cref="Section"/> and is defensive: a failure anywhere
/// yields an empty part of a section, never an exception.
/// </summary>
public static partial class Hardware
{
    private const string TpmNs = @"root\cimv2\Security\MicrosoftTpm";
    private const string SecurityCenterNs = @"root\cimv2\SecurityCenter2";
    private const string PowerNs = @"root\cimv2\power";

    // ================================================================== overview
    public static Section Overview()
    {
        var s = new Section("overview", "nav.overview", "\uE7C3");
        var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem") ?? WmiRow.Empty;
        var os = Wmi.First("SELECT * FROM Win32_OperatingSystem") ?? WmiRow.Empty;
        var product = Wmi.First("SELECT * FROM Win32_ComputerSystemProduct") ?? WmiRow.Empty;
        var enclosure = Wmi.First("SELECT * FROM Win32_SystemEnclosure") ?? WmiRow.Empty;
        var bios = Wmi.First("SELECT * FROM Win32_BIOS") ?? WmiRow.Empty;
        var board = Wmi.First("SELECT * FROM Win32_BaseBoard") ?? WmiRow.Empty;

        var computer = s.AddGroup("grp.computer");
        computer
            .AddIf("f.computerName", Environment.MachineName)
            .AddIf("f.manufacturer", cs.StrOrNull("Manufacturer"))
            .AddIf("f.model", cs.StrOrNull("Model"))
            .AddIf("f.systemFamily", cs.StrOrNull("SystemFamily"))
            .AddIf("f.sku", cs.StrOrNull("SystemSKUNumber"))
            .AddIf("f.systemType", cs.StrOrNull("SystemType"))
            .AddIf("f.pcSystemType", Lookup.PcSystemType(cs.I32("PCSystemType")))
            .AddIf("f.deviceType", DeviceKind(cs))
            .AddIf("f.serialNumber", product.StrOrNull("IdentifyingNumber"))
            .AddIf("f.uuid", product.StrOrNull("UUID"))
            .AddIf("f.chassis", Lookup.ChassisType(FirstInt(enclosure.StrOrNull("ChassisTypes"))))
            .AddIf("f.serialNumberChassis", enclosure.StrOrNull("SerialNumber"))
            .AddIf("f.domain", cs.StrOrNull("Domain"))
            .AddIf("f.loggedOnUser", cs.StrOrNull("UserName"))
            .AddIf("f.userProfile", SafeUserProfile());

        var osGroup = s.AddGroup("grp.os");
        var caption = os.StrOrNull("Caption");
        var editionId = Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "EditionID", view64: true);
        var displayVersion = Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", view64: true);
        var productName = Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName", view64: true);
        var ubr = Reg.Dword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR");
        var build = os.StrOrNull("BuildNumber");
        if (ubr is not null && !string.IsNullOrEmpty(build))
        {
            build += "." + ubr.Value.ToString(CultureInfo.InvariantCulture);
        }

        var buildNumber = os.I32("BuildNumber");
        osGroup
            .AddIf("f.osName", Fmt.Has(productName) && buildNumber is >= 22000 && productName!.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)
                ? Fmt.Edition(caption, editionId, buildNumber)
                : productName)
            .AddIf("f.osEdition", Fmt.Edition(caption, editionId, buildNumber))
            .AddIf("f.displayVersion", displayVersion)
            .AddIf("f.osVersion", os.StrOrNull("Version"))
            .AddIf("f.osBuild", build)
            .AddIf("f.osArch", os.StrOrNull("OSArchitecture"))
            .Add("f.osInstall", Fmt.DateOnly(os.StrOrNull("InstallDate")))
            .Add("f.osBoot", Fmt.DateTimeText(os.Date("LastBootUpTime") ?? Native.LastBootTime()))
            .Add("f.uptime", Fmt.Duration(TimeSpan.FromSeconds(Native.UptimeSeconds())))
            .AddIf("f.osDir", os.StrOrNull("WindowsDirectory"))
            .AddIf("f.osLocale", LocaleName(os.StrOrNull("Locale")))
            .AddIf("f.osRegistered", os.StrOrNull("RegisteredUser"))
            .AddIf("f.osOrg", os.StrOrNull("Organization"))
            .Add("f.osLicense", ActivationState())
            .AddIf("f.osExperience", Reg.Value(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ExperiencePackVersion", view64: true));

        var firmware = s.AddGroup("grp.firmware");
        firmware
            .AddIf("f.boardMfr", board.StrOrNull("Manufacturer"))
            .AddIf("f.boardProduct", board.StrOrNull("Product"))
            .AddIf("f.boardVersion", board.StrOrNull("Version"))
            .AddIf("f.boardSerial", board.StrOrNull("SerialNumber"))
            .AddIf("f.biosVendor", bios.StrOrNull("Manufacturer"))
            .AddIf("f.biosVersion", bios.StrOrNull("SMBIOSBIOSVersion"))
            .Add("f.biosDate", Fmt.DateOnly(bios.StrOrNull("ReleaseDate")))
            .Add("f.biosMode", FirmwareMode())
            .Add("f.secureBoot", SecureBootState())
            .AddIf("f.tpm", TpmSummary());

        var runtime = s.AddGroup("grp.runtime");
        var (procArch, nativeArch) = Native.ProcessArchitecture();
        runtime
            .Add("f.dotnet", Environment.Version.ToString())
            .Add("f.processArch", procArch == nativeArch
                ? procArch
                : procArch + " / " + nativeArch)
            .AddIf("f.powerPlanActive", ActivePowerPlan())
            .Add("f.elevation", Babel.L.IsChinese
                ? (Native.IsElevated() ? "已提升（管理员）" : "标准用户权限")
                : (Native.IsElevated() ? "Elevated (administrator)" : "Standard user"));

        s.Summary = string.Join(" · ", new[]
        {
            Fmt.Or(productName, caption),
            cs.StrOrNull("Manufacturer") is { } m && cs.StrOrNull("Model") is { } mo ? (m + " " + mo).Trim() : null,
            Environment.MachineName,
        }.Where(Fmt.Has));
        return s;
    }

    private static int? FirstInt(string? value)
    {
        if (!Fmt.Has(value))
        {
            return null;
        }

        var first = value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>WMI reports the locale as a hex LCID ("0804"); show the matching culture name.</summary>
    private static string? LocaleName(string? lcid)
    {
        if (!Fmt.Has(lcid))
        {
            return null;
        }

        if (int.TryParse(lcid, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(id);
                return culture.DisplayName + " (" + lcid + ")";
            }
            catch
            {
                // fall through to the raw value
            }
        }

        return lcid;
    }

    private static string? SafeUserProfile()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            return null;
        }
    }

    private static string? DeviceKind(WmiRow cs)
    {
        if (cs.I32("PCSystemType") == 1)
        {
            return Babel.L.IsChinese ? "移动设备（笔记本 / 平板）" : "Mobile (laptop / tablet)";
        }

        if (cs.I32("PCSystemType") is not null)
        {
            return Babel.L.IsChinese ? "桌面设备" : "Desktop";
        }

        var type = cs.StrOrNull("SystemType");
        return type;
    }

    private static string ActivationState()
    {
        try
        {
            var license = Wmi.First(
                "SELECT * FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
            if (license is null)
            {
                return Babel.L.IsChinese ? "未知（无许可证信息）" : "Unknown (no licence information)";
            }

            var text = license.I32("LicenseStatus") switch
            {
                0 => Babel.L.IsChinese ? "未激活" : "Not activated",
                1 => Babel.L.IsChinese ? "已激活" : "Activated",
                2 => Babel.L.IsChinese ? "宽限期" : "Grace period",
                3 => Babel.L.IsChinese ? "额外宽限期" : "Additional grace period",
                4 => Babel.L.IsChinese ? "非正版宽限期" : "Non-genuine grace period",
                5 => Babel.L.IsChinese ? "通知模式" : "Notification mode",
                6 => Babel.L.IsChinese ? "延长宽限期" : "Extended grace period",
                _ => Fmt.Unknown,
            };

            var key = license.StrOrNull("PartialProductKey");
            return Fmt.Has(key) ? text + " (" + key + ")" : text;
        }
        catch
        {
            return Fmt.Unknown;
        }
    }

    private static string FirmwareMode()
    {
        return Native.FirmwareType() switch
        {
            Native.Firmware.Uefi => "UEFI",
            Native.Firmware.Bios => "BIOS (Legacy)",
            _ => Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control", "PEFirmwareType") switch
            {
                1 => "BIOS (Legacy)",
                2 => "UEFI",
                _ => Fmt.Unknown,
            },
        };
    }

    private static string SecureBootState()
    {
        var v = Reg.Dword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        return v switch
        {
            0 => Fmt.No,
            1 => Fmt.Yes,
            _ => Fmt.Unknown,
        };
    }

    private static string? TpmSummary()
    {
        var tpm = Wmi.First("SELECT * FROM Win32_Tpm", TpmNs);
        var version = tpm?.StrOrNull("SpecVersion");
        if (!Fmt.Has(version))
        {
            return null;
        }

        return "TPM " + version!.Split(',')[0].Trim();
    }

    private static string? ActivePowerPlan()
    {
        var plans = Wmi.Query("SELECT * FROM Win32_PowerPlan", PowerNs);
        return plans.FirstOrDefault(p => p.B("IsActive") == true)?.StrOrNull("ElementName");
    }

    // ================================================================== processor
    public static Section Processor()
    {
        var s = new Section("cpu", "nav.cpu", "\uE950");
        var cpus = Wmi.Query("SELECT * FROM Win32_Processor");
        if (cpus.Count == 0)
        {
            s.Summary = Fmt.Unknown;
            return s;
        }

        foreach (var cpu in cpus)
        {
            var g = s.AddGroup("grp.cpu");
            var cores = cpu.I32("NumberOfCores");
            var threads = cpu.I32("NumberOfLogicalProcessors");
            var addressWidth = cpu.U32("AddressWidth");
            g
                .AddIf("f.cpuName", cpu.StrOrNull("Name"))
                .AddIf("f.cpuMfr", cpu.StrOrNull("Manufacturer"))
                .AddIf("f.cpuSocket", cpu.StrOrNull("SocketDesignation"))
                .AddIf("f.cpuCores", cores is null ? null : cores.Value.ToString(CultureInfo.CurrentCulture) + CoreSuffix())
                .AddIf("f.cpuThreads", threads is null ? null : threads.Value.ToString(CultureInfo.CurrentCulture) + ThreadSuffix())
                .AddIf("f.cpuBaseClock", Fmt.Ghz(cpu.U32("MaxClockSpeed")))
                .AddIf("f.cpuCurrentClock", cpu.U32("CurrentClockSpeed") is { } cc && cc > 0 && cc != cpu.U32("MaxClockSpeed") ? Fmt.Mhz(cc) : null)
                .AddIf("f.cpuExtClock", Fmt.Mhz(cpu.U32("ExtClock")))
                .AddIf("f.cpuL2", cpu.U64("L2CacheSize") is { } l2 && l2 > 0 ? Fmt.Size(l2 * 1024.0) : null)
                .AddIf("f.cpuL3", cpu.U64("L3CacheSize") is { } l3 && l3 > 0 ? Fmt.Size(l3 * 1024.0) : null)
                .AddIf("f.cpuVoltage", cpu.U32("CurrentVoltage") is { } cv and > 0 ? (cv / 10.0).ToString("N2", CultureInfo.CurrentCulture) + " V" : null)
                .AddIf("f.cpuProcessorId", cpu.StrOrNull("ProcessorId"))
                .AddIf("f.cpuFamily", Lookup.ProcessorFamily(cpu.I32("Family")))
                .AddIf("f.cpuType", Lookup.ProcessorType(cpu.I32("ProcessorType")))
                .AddIf("f.cpuRevision", cpu.U32("Revision") is { } rev ? "0x" + rev.ToString("X4") : null)
                .AddIf("f.cpuStep", cpu.StrOrNull("Stepping"))
                .AddIf("f.cpuAddressWidth", addressWidth is null ? null : addressWidth.Value + "-bit")
                .AddIf("f.cpuDataWidth", cpu.U32("DataWidth") is { } dw ? dw + "-bit" : null)
                .AddIf("f.cpuUpgrade", Lookup.UpgradeMethod(cpu.I32("UpgradeMethod")))
                .AddIf("f.cpuStatus", Lookup.Availability(cpu.I32("Availability")), tone: Lookup.AvailabilityTone(cpu.I32("Availability")));

            var features = s.AddGroup("grp.cpuFeatures");
            var load = Native.CpuLoadSample(0);
            features
                .Add("f.cpuLoad", Fmt.Pct(load))
                .AddIf("f.cpuTemperature", CpuTemperature())
                .Add("f.cpuVirtualization", Fmt.B(cpu.B("VirtualizationFirmwareEnabled")))
                .Add("f.cpuVmMonitor", Fmt.B(cpu.B("VMMonitorModeExtensions")))
                .Add("f.cpuSecondLevel", Fmt.B(cpu.B("SecondLevelAddressTranslationExtensions")))
                .Add("f.cpuDep", Fmt.B(cpu.B("DataExecutionPrevention_32BitApplications")))
                .Add("f.cpu64bit", addressWidth is null ? Fmt.Unknown : Fmt.B(addressWidth >= 64))
                .Add("f.cpuHypervisor", Fmt.B(cpu.B("HypervisorPresent")))
                .AddIf("f.cpuPower", cpu.U32("CurrentVoltage") is null ? null : null);
        }

        var perCore = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfOS_Processor");
        var coreRows = perCore
            .Where(r => Fmt.Has(r.StrOrNull("Name")) && !string.Equals(r.StrOrNull("Name"), "_Total", StringComparison.OrdinalIgnoreCase))
            .Select(r => (Name: r.Str("Name"), Load: r.D("PercentProcessorTime")))
            .Where(r => r.Load is not null)
            .OrderBy(r => ParseCoreIndex(r.Name) ?? int.MaxValue)
            .ToList();

        if (coreRows.Count > 0)
        {
            var cores = s.AddGroup("grp.cpuCores");
            foreach (var (name, loadValue) in coreRows)
            {
                var idx = ParseCoreIndex(name);
                var label = idx is null
                    ? name
                    : (Babel.L.IsChinese ? "逻辑处理器 " + idx : "Logical processor " + idx);
                cores.Add(label, Fmt.Pct(loadValue, 1), tone: loadValue >= 90 ? Tone.Warn : Tone.Normal);
            }
        }

        s.Summary = Fmt.Or(cpus[0].StrOrNull("Name"), Fmt.Unknown);
        return s;
    }

    private static string CoreSuffix() => Babel.L.IsChinese ? " 核" : " cores";

    private static string ThreadSuffix() => Babel.L.IsChinese ? " 线程" : " threads";

    private static int? ParseCoreIndex(string name) =>
        int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static string? CpuTemperature()
    {
        var zones = Wmi.Query("SELECT * FROM MSAcpi_ThermalZoneTemperature");
        var values = zones
            .Select(z => z.D("CurrentTemperature"))
            .Where(v => v is > 0)
            .Select(v => (v!.Value / 10.0) - 273.15)
            .Where(c => c is > -20 and < 150)
            .ToList();

        return values.Count == 0
            ? null
            : values.Max().ToString("N1", CultureInfo.CurrentCulture) + " °C";
    }

    // ================================================================== memory
    public static Section Memory()
    {
        var s = new Section("memory", "nav.memory", "\uEEA0");
        var status = Native.Memory();
        var array = Wmi.First("SELECT * FROM Win32_PhysicalMemoryArray");
        var modules = Wmi.Query("SELECT * FROM Win32_PhysicalMemory");

        var total = status?.TotalPhysical ?? WmiTotalMemory();

        var summary = s.AddGroup("grp.memorySummary");
        summary
            .Add("f.memTotal", Fmt.Size((double?)total))
            .AddIf("f.memAvailable", status is null ? null : Fmt.Size(status.AvailablePhysical))
            .AddIf("f.memUsed", status is null ? null : Fmt.Size(status.UsedPhysical))
            .AddIf("f.memUsage", status is null ? null : Fmt.Pct(status.LoadPercent), tone: status is { LoadPercent: >= 90 } ? Tone.Warn : Tone.Normal)
            .AddIf("f.memSlotsTotal", array?.U32("MemoryDevices")?.ToString(CultureInfo.CurrentCulture))
            .AddIf("f.memSlotsUsed", modules.Count > 0 ? modules.Count.ToString(CultureInfo.CurrentCulture) : null)
            .AddIf("f.memMaxCapacity", array?.U64("MaxCapacity") is { } max and > 0 ? Fmt.Size((double?)max) : null)
            .AddIf("f.memEcc", array?.I32("MemoryErrorCorrection") is { } ecc ? EccName(ecc) : null);

        foreach (var module in modules)
        {
            var bank = module.StrOrNull("DeviceLocator") ?? module.StrOrNull("BankLabel");
            var g = s.AddGroup("grp.memoryModule");
            g.Subtitle = bank ?? string.Empty;
            g
                .AddIf("f.memCapacity", Fmt.Size((double?)module.U64("Capacity")))
                .AddIf("f.memSlot", bank)
                .AddIf("f.memType", Lookup.MemoryType(module.I32("SMBIOSMemoryType") ?? module.I32("MemoryType")))
                .AddIf("f.memSpeed", Fmt.Mhz(module.U32("Speed")))
                .AddIf("f.memConfiguredSpeed", Fmt.Mhz(module.U32("ConfiguredClockSpeed")))
                .AddIf("f.memFormFactor", Lookup.MemoryFormFactor(module.I32("FormFactor")))
                .AddIf("f.memManufacturer", module.StrOrNull("Manufacturer"))
                .AddIf("f.memPartNumber", module.StrOrNull("PartNumber"))
                .AddIf("f.memSerial", module.StrOrNull("SerialNumber"))
                .AddIf("f.memVoltage", module.U32("ConfiguredVoltage") is { } mv and > 0 ? (mv / 1000.0).ToString("N2", CultureInfo.CurrentCulture) + " V" : null)
                .AddIf("f.memDataWidth", module.U32("DataWidth") is { } dw and > 0 ? dw + "-bit" : null)
                .AddIf("f.memTotalWidth", module.U32("TotalWidth") is { } tw and > 0 ? tw + "-bit" : null);
        }

        if (status is not null)
        {
            var page = s.AddGroup("grp.pageFile");
            page
                .Add("f.pageFileTotal", Fmt.Size(status.TotalPageFile))
                .Add("f.pageFileUsed", Fmt.Size(status.UsedPageFile));
        }

        s.Summary = Fmt.Size((double?)total)
            + (status is null ? string.Empty : " · " + Fmt.Pct(status.LoadPercent) + (Babel.L.IsChinese ? " 已用" : " used"))
            + (modules.Count > 1 ? " · " + modules.Count + (Babel.L.IsChinese ? " 条" : " modules") : string.Empty);
        return s;
    }

    private static string EccName(int code) => code switch
    {
        1 => "Other",
        2 => "Unknown",
        3 => Babel.L.IsChinese ? "无" : "None",
        4 => "Parity",
        5 => "Single-bit ECC",
        6 => "Multi-bit ECC",
        7 => "CRC",
        _ => "0x" + code.ToString("X"),
    };

    private static ulong? WmiTotalMemory()
    {
        var kb = Wmi.First("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")?.U64("TotalVisibleMemorySize");
        return kb is null ? null : kb * 1024;
    }

    // ================================================================== storage
    public static Section Storage()
    {
        var s = new Section("storage", "nav.storage", "\uEDA2");
        var disks = Wmi.Query("SELECT * FROM Win32_DiskDrive");
        var logical = Wmi.Query("SELECT * FROM Win32_LogicalDisk");
        var diskToPartition = Wmi.Query("SELECT * FROM Win32_DiskDriveToDiskPartition");
        var partitionToLogical = Wmi.Query("SELECT * FROM Win32_LogicalDiskToPartition");
        var perf = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk");

        foreach (var disk in disks)
        {
            var model = disk.StrOrNull("Model");
            var letters = LettersForDisk(disk.StrOrNull("DeviceID"), diskToPartition, partitionToLogical);
            var g = s.AddGroup("grp.volume");
            g.Subtitle = Fmt.Or(model, Fmt.Unknown);
            g
                .AddIf("f.driveModel", model)
                .AddIf("f.driveSize", Fmt.Size((double?)disk.U64("Size")))
                .AddIf("f.driveInterface", disk.StrOrNull("InterfaceType"))
                .AddIf("f.driveBusType", BusTypeOf(disk.StrOrNull("PNPDeviceID")))
                .AddIf("f.driveType", Lookup.DriveMediaType(disk.I32("MediaType")))
                .AddIf("f.driveSerial", disk.StrOrNull("SerialNumber"))
                .AddIf("f.driveFirmware", disk.StrOrNull("FirmwareRevision"))
                .AddIf("f.drivePartitions", disk.U32("Partitions")?.ToString(CultureInfo.CurrentCulture))
                .AddIf("f.driveBytesPerSector", disk.U32("BytesPerSector")?.ToString("N0", CultureInfo.CurrentCulture))
                .AddIf("f.volumeLetter", letters.Count > 0 ? string.Join(", ", letters) : null)
                .AddIf("f.drivePhysical", disk.StrOrNull("DeviceID"))
                .AddIf("f.driveSmart", SmartStatus(disk.StrOrNull("Status")), tone: SmartTone(disk.StrOrNull("Status")));
        }

        foreach (var vol in logical
                     .Where(v => v.I32("DriveType") is 2 or 3 or 4)
                     .OrderBy(v => v.StrOrNull("DeviceID")))
        {
            var letter = vol.StrOrNull("DeviceID") ?? "?";
            var total = vol.U64("Size");
            var free = vol.U64("FreeSpace");
            var used = total is not null && free is not null ? total - free : null;
            double? pct = total is > 0 && used is not null ? used.Value * 100.0 / total.Value : null;

            var g = s.AddGroup("grp.volume");
            g.Subtitle = letter + (Fmt.Has(vol.StrOrNull("VolumeName")) ? "  " + vol.StrOrNull("VolumeName") : string.Empty);
            g
                .AddIf("f.volumeLetter", letter)
                .AddIf("f.volumeLabel", vol.StrOrNull("VolumeName"))
                .AddIf("f.volumeFs", vol.StrOrNull("FileSystem"))
                .Add("f.volumeTotal", Fmt.Size((double?)total))
                .Add("f.volumeFree", Fmt.Size((double?)free))
                .Add("f.volumeUsed", Fmt.Size((double?)used))
                .AddIf("f.volumeUsage", Fmt.Pct(pct, 1), tone: pct is >= 90 ? Tone.Warn : Tone.Normal)
                .AddIf("f.volumeCompressed", vol.B("Compressed") is { } c ? Fmt.B(c) : null)
                .AddIf("f.volumeType", vol.I32("DriveType") switch
                {
                    2 => Babel.L.IsChinese ? "可移动磁盘" : "Removable disk",
                    3 => Babel.L.IsChinese ? "本地磁盘" : "Local disk",
                    4 => Babel.L.IsChinese ? "网络驱动器" : "Network drive",
                    _ => null,
                })
                .AddIf("f.volumeBoot", vol.B("BootVolume") is { } b ? Fmt.B(b) : null);
        }

        var active = Wmi.Query("SELECT * FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk")
            .Where(r => !string.Equals(r.StrOrNull("Name"), "_Total", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (active.Count > 0)
        {
            var table = s.AddGroup("grp.diskPerf");
            foreach (var row in active.Take(8))
            {
                var read = row.U64("DiskReadBytesPerSec") ?? 0;
                var write = row.U64("DiskWriteBytesPerSec") ?? 0;
                table.Add(
                    row.StrOrNull("Name") ?? Fmt.Unknown,
                    (read == 0 ? "0 B/s" : Fmt.Size(read) + "/s") + "   " + (write == 0 ? "0 B/s" : Fmt.Size(write) + "/s"));
            }
        }

        var fixedDrives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
        s.Summary = string.Join(" · ", fixedDrives.Select(d =>
            d.Name.TrimEnd('\\') + " " + Fmt.Size((double?)(d.TotalSize - d.AvailableFreeSpace)) + " / " + Fmt.Size(d.TotalSize)));
        return s;
    }

    private static string? SmartStatus(string? status) => status switch
    {
        null or "" => null,
        "OK" => Fmt.Yes,
        "Degraded" => Babel.L.IsChinese ? "已降级" : "Degraded",
        "Stressed" => Babel.L.IsChinese ? "压力状态" : "Stressed",
        "Pred Fail" => Babel.L.IsChinese ? "即将故障" : "Predictive failure",
        "Error" => Babel.L.IsChinese ? "错误" : "Error",
        _ => status,
    };

    private static Tone SmartTone(string? status) => status switch
    {
        "OK" => Tone.Good,
        "Degraded" or "Stressed" => Tone.Warn,
        "Pred Fail" or "Error" => Tone.Bad,
        _ => Tone.Normal,
    };

    private static string? BusTypeOf(string? pnpDeviceId)
    {
        if (!Fmt.Has(pnpDeviceId))
        {
            return null;
        }

        var id = pnpDeviceId!;
        if (id.StartsWith("SCSI", StringComparison.OrdinalIgnoreCase))
        {
            return "SCSI / SAS";
        }

        if (id.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB";
        }

        if (id.Contains("NVME", StringComparison.OrdinalIgnoreCase))
        {
            return "NVMe";
        }

        if (id.StartsWith("IDE", StringComparison.OrdinalIgnoreCase) || id.StartsWith("PCIIDE", StringComparison.OrdinalIgnoreCase))
        {
            return "ATA / SATA";
        }

        if (id.StartsWith("PCI", StringComparison.OrdinalIgnoreCase))
        {
            return "PCIe / SATA";
        }

        return null;
    }

    private static List<string> LettersForDisk(string? deviceId, List<WmiRow> diskToPartition, List<WmiRow> partitionToLogical)
    {
        var letters = new List<string>();
        if (!Fmt.Has(deviceId))
        {
            return letters;
        }

        var partitionIds = diskToPartition
            .Where(r => SameWmiPath(r.StrOrNull("Antecedent"), deviceId))
            .Select(r => r.StrOrNull("Dependent"))
            .Where(Fmt.Has)
            .ToList();

        foreach (var part in partitionIds)
        {
            foreach (var mapping in partitionToLogical.Where(r => SameWmiPath(r.StrOrNull("Antecedent"), part)))
            {
                var dependent = mapping.StrOrNull("Dependent");
                if (!Fmt.Has(dependent))
                {
                    continue;
                }

                var start = dependent!.LastIndexOf('=');
                if (start >= 0)
                {
                    letters.Add(dependent[(start + 1)..].Trim('"'));
                }
            }
        }

        return letters.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool SameWmiPath(string? path, string? wanted) =>
        Fmt.Has(path) && Fmt.Has(wanted) && string.Equals(path!.Trim(), wanted!.Trim(), StringComparison.OrdinalIgnoreCase);
}
