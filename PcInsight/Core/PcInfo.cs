using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PcInsight.Core;

/// <summary>Runs every collector and assembles the snapshot shown by the UI.</summary>
public static class PcInfo
{
    private static bool Zh => Babel.L.IsChinese;

    private static string Pair(string zh, string en) => Zh ? zh : en;

    /// <summary>
    /// The pages the app shows, in navigation order, with the collector that fills each of them and
    /// the resource keys used as placeholder rows while it is still running.
    /// </summary>
    public static readonly (string Key, string Title, string Glyph, string[] PendingLabels, Func<Section> Run)[] Jobs =
    {
        ("overview", "nav.overview", "\uE7C3", new[] { "f.computerName", "f.osName", "f.cpuName", "f.memTotal" }, Hardware.Overview),
        ("cpu", "nav.cpu", "\uE950", new[] { "f.cpuName", "f.cpuCores", "f.cpuBaseClock", "f.cpuLoad" }, Hardware.Processor),
        ("memory", "nav.memory", "\uEEA0", new[] { "f.memTotal", "f.memType", "f.memSpeed", "f.memSlotsUsed" }, Hardware.Memory),
        ("storage", "nav.storage", "\uEDA2", new[] { "f.driveModel", "f.volumeLetter", "f.volumeTotal", "f.volumeFree" }, Hardware.Storage),
        ("graphics", "nav.graphics", "\uE7F4", new[] { "f.gpuName", "f.gpuDriver", "f.gpuResolution", "f.monitorName" }, Hardware.Graphics),
        ("network", "nav.network", "\uE968", new[] { "f.netName", "f.netIpv4", "f.netMac", "f.netGateway" }, Hardware.Network),
        ("audio", "nav.audio", "\uE8D6", new[] { "f.audioName", "f.audioMfr", "f.audioKind", "f.audioStatus" }, Hardware.Audio),
        ("battery", "nav.battery", "\uE83F", new[] { "f.batCharge", "f.batChemistry", "f.batVoltage", "f.batAc" }, Hardware.Battery),
        ("usb", "nav.usb", "\uE88E", new[] { "f.usbName", "f.usbClass", "f.usbStatus", "f.usbConnected" }, Hardware.Usb),
        ("printers", "nav.printers", "\uE749", new[] { "f.printerName", "f.printerPort", "f.printerDriver", "f.printerStatus" }, Hardware.Printers),
        ("security", "nav.security", "\uE72E", new[] { "f.tpmPresent", "f.secureBootState", "f.defenderRealtime", "f.uac" }, Hardware.Security),
        ("sensors", "nav.sensors", "\uE9D9", new[] { "f.sensorName", "f.sensorValue", "f.sensorTemp", "f.sensorFan" }, Hardware.Sensors),
        ("software", "nav.software", "\uEC7A", new[] { "f.swName", "f.swVersion", "f.swPublisher", "f.swInstallDate" }, Software.Programs),
        ("updates", "nav.updates", "\uE895", new[] { "f.updateHotfix", "f.updateDesc", "f.updateDate", "f.updateBy" }, Software.Updates),
        ("services", "nav.services", "\uE9F5", new[] { "f.svcDisplay", "f.svcName", "f.svcState", "f.svcStart" }, Software.Services),
        ("startup", "nav.startup", "\uE7C4", new[] { "f.startName", "f.startCommand", "f.startSource", "f.startUser" }, Software.Startup),
        ("drivers", "nav.drivers", "\uE964", new[] { "f.driverDisplay", "f.driverClass", "f.driverVersion", "f.driverDate" }, Software.Drivers),
        ("processes", "nav.processes", "\uE9F5", new[] { "f.procName", "f.procPid", "f.procMemory", "f.procCompany" }, Software.Processes),
        ("environment", "nav.environment", "\uE756", new[] { "f.envName", "f.envValue", "f.envScope", "f.pathVar" }, Software.Environment_),
    };

    /// <summary>Empty placeholder section, shown before its collector has produced anything.</summary>
    public static Section CreatePending(string key, string title, string glyph, string[] pendingLabels)
    {
        var section = new Section(key, title, glyph)
        {
            IsLoading = true,
            PendingLabels = pendingLabels,
        };

        var group = section.AddGroup("status.loading");
        foreach (var label in pendingLabels)
        {
            group.Add(label, null);
        }

        return section;
    }

    /// <summary>Section keys belonging to each navigation group.</summary>
    public static readonly string[] HardwareKeys =
    {
        "overview", "cpu", "memory", "storage", "graphics", "network",
        "audio", "battery", "usb", "printers", "security", "sensors",
    };

    public static readonly string[] SoftwareKeys =
    {
        "software", "updates", "services", "startup", "drivers", "processes", "environment",
    };

    /// <summary>
    /// Run every collector in parallel and report each section the moment it is ready, so the shell
    /// can fill its pages progressively instead of waiting for the slowest collector.
    /// </summary>
    public static async Task<Snapshot> ScanAsync(IProgress<Section>? sectionReady = null)
    {
        var sw = Stopwatch.StartNew();
        var snapshot = new Snapshot();
        var results = new Section?[Jobs.Length];
        var raw = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var tasks = Jobs.Select((job, index) => Task.Run(() =>
        {
            try
            {
                var section = job.Run();
                section.IsLoading = false;
                section.PendingLabels = Array.Empty<string>();
                results[index] = section;
                sectionReady?.Report(section);
            }
            catch (Exception ex)
            {
                var failed = new Section(job.Key, job.Title, job.Glyph)
                {
                    Severity = Condition.Bad,
                    IsLoading = false,
                };

                failed.AddGroup("grp.sensorOther").Add("status.error", ex.Message, tone: Tone.Bad);
                results[index] = failed;
                raw["error." + job.Key] = ex.GetType().Name + ": " + ex.Message;
                sectionReady?.Report(failed);
            }
        })).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var section in results.Where(s => s is not null))
        {
            snapshot.Sections.Add(section!);
        }

        foreach (var kv in raw)
        {
            snapshot.Raw[kv.Key] = kv.Value;
        }

        MapLists(snapshot);
        snapshot.Duration = sw.Elapsed;
        return snapshot;
    }

    /// <summary>Group the sections into the two navigation categories used by the shell.</summary>
    private static void MapLists(Snapshot snapshot)
    {
        foreach (var s in snapshot.Sections)
        {
            if (HardwareKeys.Contains(s.Key))
            {
                snapshot.Hardware.Add(s);
            }
            else if (SoftwareKeys.Contains(s.Key))
            {
                snapshot.Software.Add(s);
            }
        }
    }

    /// <summary>Cheap live refresh of the volatile values shown on the overview page.</summary>
    public static (double? Cpu, ulong? UsedMemory, ulong? TotalMemory, ulong? Uptime) LiveValues()
    {
        var mem = Native.Memory();
        return (Native.CpuLoadSample(300), mem?.UsedPhysical, mem?.TotalPhysical, Native.UptimeSeconds());
    }

    /// <summary>One-line "what machine is this" description used in the title bar.</summary>
    public static string MachineHeadline()
    {
        try
        {
            var cs = Wmi.First("SELECT * FROM Win32_ComputerSystem");
            var os = Wmi.First("SELECT * FROM Win32_OperatingSystem");
            var parts = new[]
            {
                Environment.MachineName,
                cs?.StrOrNull("Manufacturer"),
                cs?.StrOrNull("Model"),
                os?.StrOrNull("Caption"),
            };
            return string.Join(" · ", parts.Where(Fmt.Has));
        }
        catch
        {
            return Environment.MachineName;
        }
    }

    /// <summary>True when a CIM query can be executed at all (surfaced as a warning banner otherwise).</summary>
    public static bool WmiAvailable() => Wmi.Available();

    public static string PermissionHint() => Native.IsElevated()
        ? Pair("以管理员权限运行，可读取的信息最完整。", "Running elevated: the most complete information is available.")
        : Pair("当前为普通权限，部分信息（如 BitLocker、TPM 详情）可能不可见。以管理员身份运行可获得完整数据。",
            "Running as a standard user; some information (BitLocker, TPM details) may be hidden. Run as administrator for complete data.");
}
