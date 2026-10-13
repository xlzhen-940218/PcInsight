using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace PcInsight.Core;

/// <summary>Software collectors: installed programs, updates, services, startup items, drivers, processes, environment.</summary>
public static class Software
{
    private static bool Zh => Babel.L.IsChinese;

    private static string Pair(string zh, string en) => Zh ? zh : en;

    // ================================================================== installed programs
    public static Section Programs()
    {
        var s = new Section("software", "nav.software", "\uEC7A");
        s.Columns.Add(new TableColumn("f.swName", 3.0));
        s.Columns.Add(new TableColumn("f.swVersion", 1.4));
        s.Columns.Add(new TableColumn("f.swPublisher", 2.2));
        s.Columns.Add(new TableColumn("f.swInstallDate", 1.2));
        s.Columns.Add(new TableColumn("f.swSize", 1.0, right: true));

        var programs = new List<(string Name, string Version, string Publisher, string Date, long? Size, string? Location, string Source)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hiveSub, view) in new[]
                 {
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.RegistryView.Registry64),
                     (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.RegistryView.Registry64),
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.RegistryView.Registry32),
                 })
        {
            CollectUninstall(hiveSub, view, "desktop", programs, seen);
        }

        // per-user installs
        CollectUninstall(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", Microsoft.Win32.RegistryView.Registry64, "user", programs, seen, RegistryHive.CurrentUser);

        var storeApps = StorePackages();

        var summary = s.AddGroup("grp.softwareSummary");
        summary
            .Add("f.swCount", programs.Count.ToString("N0", CultureInfo.CurrentCulture))
            .AddIf("f.swPublisherCount", programs.Select(p => p.Publisher).Where(Fmt.Has).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0", CultureInfo.CurrentCulture))
            .AddIf("f.swDesktopApps", programs.Count(p => p.Source == "desktop").ToString("N0", CultureInfo.CurrentCulture))
            .AddIf("f.swStoreApps", storeApps.Count > 0 ? storeApps.Count.ToString("N0", CultureInfo.CurrentCulture) : null);

        foreach (var p in programs
                     .OrderByDescending(p => p.Date)
                     .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            s.AddRow(
                Tone.Normal,
                p.Name,
                p.Version,
                p.Publisher,
                p.Date,
                p.Size is null ? string.Empty : Fmt.Size(p.Size.Value, 0));
        }

        s.Summary = programs.Count.ToString("N0", CultureInfo.CurrentCulture) + Pair(" 个程序", " programs")
            + (storeApps.Count > 0 ? " · " + storeApps.Count + Pair(" 个商店应用", " store apps") : string.Empty);
        return s;
    }

    private static void CollectUninstall(
        string subKey,
        Microsoft.Win32.RegistryView view,
        string source,
        List<(string Name, string Version, string Publisher, string Date, long? Size, string? Location, string Source)> programs,
        HashSet<string> seen,
        RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(subKey, false);
            if (uninstall is null)
            {
                return;
            }

            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var app = uninstall.OpenSubKey(name, false);
                    if (app is null)
                    {
                        continue;
                    }

                    var display = app.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        continue;
                    }

                    // Skip system components and update stubs; they only add noise.
                    var isComponent = app.GetValue("SystemComponent") is int sc && sc == 1;
                    var hasParent = app.GetValue("ParentKeyName") is string pk && !string.IsNullOrWhiteSpace(pk);

                    var version = app.GetValue("DisplayVersion") as string ?? string.Empty;
                    var publisher = app.GetValue("Publisher") as string ?? string.Empty;
                    var date = Fmt.RegistryDate(app.GetValue("InstallDate") as string);
                    long? size = app.GetValue("EstimatedSize") is { } es ? Convert.ToInt64(es, CultureInfo.InvariantCulture) * 1024 : null;
                    var location = app.GetValue("InstallLocation") as string;
                    var entrySize = size is > 4096 ? size : 0;

                    var key = display!.Trim() + "|" + version + (isComponent || hasParent ? "|noise" : string.Empty);
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    programs.Add((display.Trim(), version, publisher, date == Fmt.Unknown ? string.Empty : date, entrySize, location, source));
                }
                catch
                {
                    // skip broken entries
                }
            }
        }
        catch
        {
            // hive not accessible
        }
    }

    private static List<string> StorePackages()
    {
        var list = new List<string>();

        // Win32_Product is deliberately never queried: enumerating it triggers MSI
        // reconfiguration for every installed package. The AppModel repository is read-only.
        try
        {
            const string key = @"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
            foreach (var sub in Reg.SubKeys(RegistryHive.LocalMachine, key, view64: true).Take(4000))
            {
                if (sub.Contains('_'))
                {
                    list.Add(sub);
                }
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    // ================================================================== updates
    public static Section Updates()
    {
        var s = new Section("updates", "nav.updates", "\uE895");
        var hotfixes = Wmi.Query("SELECT * FROM Win32_QuickFixEngineering");
        var ordered = hotfixes
            .OrderByDescending(h => h.StrOrNull("InstalledOn") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var summary = s.AddGroup("grp.update");
        summary.Items.Clear();
        summary
            .Add("f.updateCount", ordered.Count.ToString("N0", CultureInfo.CurrentCulture))
            .AddIf("f.updateLatest", ordered.FirstOrDefault()?.StrOrNull("HotFixID"));

        foreach (var h in ordered.Take(200))
        {
            var g = s.AddGroup("grp.update");
            g.Subtitle = Fmt.Or(h.StrOrNull("HotFixID"), Fmt.Unknown);
            g
                .AddIf("f.updateHotfix", h.StrOrNull("HotFixID"))
                .AddIf("f.updateDesc", h.StrOrNull("Description"))
                .AddIf("f.updateBy", h.StrOrNull("InstalledBy"))
                .Add("f.updateDate", Fmt.Or(h.StrOrNull("InstalledOn"), Fmt.Unknown));
        }

        s.Summary = ordered.Count + Pair(" 条更新记录", " updates");
        return s;
    }

    // ================================================================== services
    public static Section Services()
    {
        var s = new Section("services", "nav.services", "\uE9F5");
        s.Columns.Add(new TableColumn("f.svcDisplay", 3.0));
        s.Columns.Add(new TableColumn("f.svcName", 2.2));
        s.Columns.Add(new TableColumn("f.svcState", 1.0));
        s.Columns.Add(new TableColumn("f.svcStart", 1.0));
        s.Columns.Add(new TableColumn("f.svcAccount", 1.4));

        var services = Wmi.Query("SELECT * FROM Win32_Service");
        var running = services.Count(sv => string.Equals(sv.StrOrNull("State"), "Running", StringComparison.OrdinalIgnoreCase));

        var summary = s.AddGroup("grp.service");
        summary.Items.Clear();
        summary
            .Add("f.svcCount", services.Count.ToString("N0", CultureInfo.CurrentCulture))
            .Add("f.svcRunning", running.ToString("N0", CultureInfo.CurrentCulture));

        foreach (var sv in services
                     .OrderBy(sv => sv.StrOrNull("DisplayName") ?? sv.StrOrNull("Name") ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
        {
            var state = sv.StrOrNull("State");
            var tone = string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase) ? Tone.Good : Tone.Muted;
            s.AddRow(
                tone,
                sv.StrOrNull("DisplayName") ?? sv.StrOrNull("Name") ?? string.Empty,
                sv.StrOrNull("Name") ?? string.Empty,
                Lookup.ServiceState(state),
                Lookup.ServiceStartMode(sv.StrOrNull("StartMode")),
                sv.StrOrNull("StartName") ?? string.Empty);
        }

        s.Summary = services.Count + Pair(" 个服务（", " services (") + running + Pair(" 个运行中）", " running)");
        return s;
    }

    // ================================================================== startup items
    public static Section Startup()
    {
        var s = new Section("startup", "nav.startup", "\uE7C4");
        s.Columns.Add(new TableColumn("f.startName", 2.0));
        s.Columns.Add(new TableColumn("f.startCommand", 4.0));
        s.Columns.Add(new TableColumn("f.startSource", 1.6));
        s.Columns.Add(new TableColumn("f.startUser", 1.2));

        var entries = new List<(string Name, string Command, string Source, string User, bool Enabled)>();

        void FromRunKey(RegistryHive hive, string path, string source, string user)
        {
            foreach (var (name, value) in Reg.Values(hive, path))
            {
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (name.Equals("(Default)", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add((name, value, source, user, true));
            }
        }

        FromRunKey(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run", Pair("所有用户", "All users"));
        FromRunKey(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM\\RunOnce", Pair("所有用户", "All users"));
        FromRunKey(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM\\Run (32-bit)", Pair("所有用户", "All users"));
        FromRunKey(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU\\Run", Environment.UserName);
        FromRunKey(RegistryHive.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU\\RunOnce", Environment.UserName);

        // Startup folders
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                 })
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(folder).Take(100))
                {
                    entries.Add((Path.GetFileNameWithoutExtension(file), file, Pair("启动文件夹", "Startup folder"), Pair("当前用户", "Current user"), true));
                }
            }
            catch
            {
                // ignore
            }
        }

        // enabled/disabled state recorded by Task Manager
        var approved = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, hive, label) in new[]
                 {
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", RegistryHive.CurrentUser, "HKCU"),
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32", RegistryHive.CurrentUser, "HKCU32"),
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", RegistryHive.CurrentUser, "HKCUStartup"),
                     (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", RegistryHive.LocalMachine, "HKLM"),
                 })
        {
            try
            {
                using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Default);
                using var key = baseKey.OpenSubKey(path, false);
                if (key is null)
                {
                    continue;
                }

                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is byte[] bytes && bytes.Length > 0)
                    {
                        approved[name] = (bytes[0] & 0x01) == 0;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        var summary = s.AddGroup("grp.startup");
        summary.Items.Clear();
        summary.Add("f.startCount", entries.Count.ToString("N0", CultureInfo.CurrentCulture));

        foreach (var e in entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var enabled = approved.TryGetValue(e.Name, out var state) ? state : e.Enabled;
            s.AddRow(
                enabled ? Tone.Normal : Tone.Muted,
                e.Name,
                Fmt.Truncate(e.Command, 220),
                e.Source,
                e.User);
        }

        var disabled = entries.Count(e => approved.TryGetValue(e.Name, out var st) && !st);
        s.Summary = entries.Count + Pair(" 个启动项", " startup entries")
            + (disabled > 0 ? Pair("（", " (") + disabled + Pair(" 个已禁用）", " disabled)") : string.Empty);
        return s;
    }

    // ================================================================== drivers
    public static Section Drivers()
    {
        var s = new Section("drivers", "nav.drivers", "\uE964");
        s.Columns.Add(new TableColumn("f.driverName", 3.2));
        s.Columns.Add(new TableColumn("f.driverClass", 1.4));
        s.Columns.Add(new TableColumn("f.driverVersion", 1.4));
        s.Columns.Add(new TableColumn("f.driverDate", 1.1));
        s.Columns.Add(new TableColumn("f.driverVendor", 2.0));
        s.Columns.Add(new TableColumn("f.driverSigned", 1.0));

        var drivers = Wmi.Query("SELECT * FROM Win32_PnPSignedDriver");
        var summary = s.AddGroup("grp.driver");
        summary.Items.Clear();
        summary.Add("f.driverCount", drivers.Count.ToString("N0", CultureInfo.CurrentCulture));

        var unsigned = 0;
        var rows = new List<(string Name, string Class, string Version, string Date, string Vendor, string Signed, Tone Tone)>();

        foreach (var d in drivers)
        {
            var name = d.StrOrNull("DeviceName") ?? d.StrOrNull("DriverName") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var signer = d.StrOrNull("Signer");
            var isSigned = Fmt.Has(signer);
            if (!isSigned)
            {
                unsigned++;
            }

            rows.Add((
                name,
                d.StrOrNull("DeviceClass") ?? string.Empty,
                d.StrOrNull("DriverVersion") ?? string.Empty,
                Fmt.DateOnly(d.StrOrNull("DriverDate")),
                d.StrOrNull("DriverProviderName") ?? string.Empty,
                isSigned ? Fmt.Yes : Pair("未签名", "Unsigned"),
                isSigned ? Tone.Normal : Tone.Warn));
        }

        foreach (var r in rows.OrderBy(r => r.Class, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            s.AddRow(r.Tone, r.Name, r.Class, r.Version, r.Date, r.Vendor, r.Signed);
        }

        s.Severity = unsigned > 0 ? Condition.Info : Condition.Ok;
        s.Summary = rows.Count + Pair(" 个驱动", " drivers")
            + (unsigned > 0 ? Pair("，其中 ", ", ") + unsigned + Pair(" 个未签名", " unsigned") : string.Empty);
        return s;
    }

    // ================================================================== processes
    public static Section Processes()
    {
        var s = new Section("processes", "nav.processes", "\uE9F5");
        s.Columns.Add(new TableColumn("f.procName", 2.6));
        s.Columns.Add(new TableColumn("f.procPid", 0.9, right: true));
        s.Columns.Add(new TableColumn("f.procMemory", 1.3, right: true));
        s.Columns.Add(new TableColumn("f.procPrivate", 1.3, right: true));
        s.Columns.Add(new TableColumn("f.procThreads", 0.9, right: true));
        s.Columns.Add(new TableColumn("f.procCompany", 2.2));

        var rows = new List<(string Name, int Pid, long Working, long Private, int Threads, int Handles, string Company, string Path, Tone Tone)>();
        var totalWorking = 0L;

        // Several processes normally share one executable (browsers, Electron apps, svchost) and
        // reading a version resource is the expensive part, so the result is cached per path.
        var companyByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                string? path = null;
                string company = string.Empty;
                try
                {
                    path = p.MainModule?.FileName;
                }
                catch
                {
                    // access denied for protected processes
                }

                if (Fmt.Has(path))
                {
                    if (!companyByPath.TryGetValue(path!, out var cachedCompany))
                    {
                        cachedCompany = FileInfoResult.Read(path).Company ?? string.Empty;
                        companyByPath[path!] = cachedCompany;
                    }

                    company = cachedCompany;
                }

                var working = p.WorkingSet64;
                totalWorking += working;
                rows.Add((
                    name,
                    p.Id,
                    working,
                    p.PrivateMemorySize64,
                    SafeThreads(p),
                    SafeHandles(p),
                    company,
                    path ?? string.Empty,
                    working > 1024L * 1024 * 1024 ? Tone.Warn : Tone.Normal));
            }
            catch
            {
                // process exited while enumerating
            }
            finally
            {
                p.Dispose();
            }
        }

        var summary = s.AddGroup("grp.process");
        summary.Items.Clear();
        summary
            .Add("f.procCount", rows.Count.ToString("N0", CultureInfo.CurrentCulture))
            .Add("f.procTotalMem", Fmt.Size(totalWorking));

        foreach (var r in rows.OrderByDescending(r => r.Working).Take(400))
        {
            s.AddRow(
                r.Tone,
                r.Name,
                r.Pid.ToString(CultureInfo.CurrentCulture),
                Fmt.Size(r.Working),
                Fmt.Size(r.Private),
                r.Threads.ToString(CultureInfo.CurrentCulture),
                r.Company);
        }

        s.Summary = rows.Count + Pair(" 个进程 · 共占用 ", " processes · ") + Fmt.Size(totalWorking);
        return s;
    }

    private static int SafeThreads(Process p)
    {
        try
        {
            return p.Threads.Count;
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeHandles(Process p)
    {
        try
        {
            return p.HandleCount;
        }
        catch
        {
            return 0;
        }
    }

    // ================================================================== environment
    public static Section Environment_()
    {
        var s = new Section("environment", "nav.environment", "\uE756");

        var machine = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Reg.Values(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
        {
            machine[name] = value;
        }

        var user = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Reg.Values(RegistryHive.CurrentUser, @"Environment"))
        {
            user[name] = value;
        }

        var machineGroup = s.AddGroup("grp.env");
        machineGroup.Subtitle = Pair("系统", "Machine");
        foreach (var kv in machine)
        {
            machineGroup.Add(kv.Key, Fmt.Truncate(kv.Value, 500));
        }

        var userGroup = s.AddGroup("grp.env");
        userGroup.Subtitle = Pair("用户", "User");
        foreach (var kv in user)
        {
            userGroup.Add(kv.Key, Fmt.Truncate(kv.Value, 500));
        }

        var pathGroup = s.AddGroup("grp.pathVar");
        foreach (var part in (System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            pathGroup.Add(Fmt.Truncate(part, 120), Fmt.Unknown);
        }

        var runtimeGroup = s.AddGroup("grp.runtime");
        runtimeGroup.Items.Clear();
        runtimeGroup
            .Add("f.dotnet", System.Environment.Version.ToString())
            .Add("f.dotnetList", DotnetRuntimes())
            .Add("f.processArch", Native.ProcessArchitecture().Process)
            .Add("f.osDir", System.Environment.SystemDirectory);

        s.Summary = machine.Count + Pair(" 个系统变量 · ", " machine · ") + user.Count + Pair(" 个用户变量", " user");
        return s;
    }

    private static string? DotnetRuntimes()
    {
        var root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "dotnet", "shared");
        try
        {
            if (!Directory.Exists(root))
            {
                return null;
            }

            var list = new List<string>();
            foreach (var kind in Directory.GetDirectories(root))
            {
                foreach (var version in Directory.GetDirectories(kind))
                {
                    list.Add(Path.GetFileName(kind) + " " + Path.GetFileName(version));
                }
            }

            return list.Count == 0 ? null : Fmt.Truncate(string.Join(", ", list.OrderBy(x => x)), 400);
        }
        catch
        {
            return null;
        }
    }
}
