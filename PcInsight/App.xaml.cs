using System;
using System.Linq;
using Microsoft.UI.Xaml;
using PcInsight.Views;
using PcInsight.Core;

namespace PcInsight;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();

        if (Array.Exists(commandLine, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            RunSelfTest();
            return;
        }

        var uiCheck = Array.Exists(commandLine, a => a.Equals("--uicheck", StringComparison.OrdinalIgnoreCase))
            || Array.Exists(commandLine, a => a.StartsWith("--uicheck=", StringComparison.OrdinalIgnoreCase));
        if (uiCheck)
        {
            PcInsight.Core.UiLog.Enabled = true;
        }

        // --uicheck=<seconds> inspects the window after that delay, which is how the progressive
        // loading behaviour is verified (for example --uicheck=3 should still show spinners).
        var settle = 20;
        var uiCheckArg = Array.Find(commandLine, a => a.StartsWith("--uicheck=", StringComparison.OrdinalIgnoreCase));
        if (uiCheckArg is not null && int.TryParse(uiCheckArg["--uicheck=".Length..], out var parsed) && parsed > 0)
        {
            settle = parsed;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();

        if (uiCheck)
        {
            _ = RunUiCheckAsync(settle);
        }
    }

    /// <summary>Headless render verification: builds the real window, then inspects and exits.</summary>
    private async Task RunUiCheckAsync(int settleSeconds)
    {
        string report;
        try
        {
            await Task.Delay(500);
            report = await MainWindow!.RunUiCheckAsync(settleSeconds);
        }
        catch (Exception ex)
        {
            report = "UI CHECK FAILED: " + ex;
        }

        try
        {
            var path = System.IO.Path.Combine(PcInsight.Core.AppPaths.ExecutableDirectory, "pcinsight-uicheck.txt");
            System.IO.File.WriteAllText(path, report);
            UiLog.Write("ui check report written to " + path);
        }
        catch
        {
            // ignore
        }

        // Write UTF-8 bytes straight to stdout: a GBK console must not be able to mangle the
        // diagnostic output when it is piped into another tool.
        try
        {
            using var stdout = Console.OpenStandardOutput();
            using var writer = new System.IO.StreamWriter(stdout, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
            writer.Write(report);
        }
        catch
        {
            // ignore
        }

        Environment.Exit(0);
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        UiLog.Write("UNHANDLED: " + e.Message + Environment.NewLine + e.Exception);
        e.Handled = true;
    }

    /// <summary>
    /// Headless verification mode: runs every collector, prints a per-section summary and writes the
    /// full snapshot as JSON next to the executable. Used to validate the data layer without a UI.
    /// </summary>
    private void RunSelfTest()
    {
        var buffer = new System.Text.StringBuilder();
        try
        {
            var snapshot = PcInfo.ScanAsync().GetAwaiter().GetResult();
            buffer.AppendLine("PC Insight self-test / 电脑信息全览 自检");
            buffer.AppendLine("generated: " + snapshot.GeneratedAt.ToString("s"));
            buffer.AppendLine("elapsed  : " + snapshot.Duration.TotalSeconds.ToString("N2") + " s");
            buffer.AppendLine("hardware : " + snapshot.Hardware.Count + " sections");
            buffer.AppendLine("software : " + snapshot.Software.Count + " sections");
            buffer.AppendLine();

            var totalItems = 0;
            foreach (var s in snapshot.Sections)
            {
                var stored = s.Groups.Sum(g => g.Items.Count(i => Fmt.Has(i.Store)));
                totalItems += stored + s.Rows.Count;
                buffer.AppendLine(string.Format(
                    "{0,-12} groups={1,-3} items={2,-4} rows={3,-4} sev={4,-5} summary={5}",
                    s.Key,
                    s.Groups.Count,
                    stored,
                    s.Rows.Count,
                    s.Severity,
                    Fmt.Truncate(s.Summary, 90)));
            }

            buffer.AppendLine();
            buffer.AppendLine("total meaningful values: " + totalItems);

            var json = Export.ToJson(snapshot);
            var outPath = System.IO.Path.Combine(PcInsight.Core.AppPaths.ExecutableDirectory, "pcinsight-dump.json");
            System.IO.File.WriteAllText(outPath, json);
            buffer.AppendLine("json: " + outPath + " (" + json.Length + " bytes)");

            var textPath = System.IO.Path.Combine(PcInsight.Core.AppPaths.ExecutableDirectory, "pcinsight-dump.txt");
            System.IO.File.WriteAllText(textPath, snapshot.ToPlainText());
            buffer.AppendLine("text: " + textPath);
        }
        catch (Exception ex)
        {
            buffer.AppendLine("SELFTEST FAILED: " + ex);
        }

        var reportPath = System.IO.Path.Combine(PcInsight.Core.AppPaths.ExecutableDirectory, "pcinsight-selftest.txt");
        try
        {
            System.IO.File.WriteAllText(reportPath, buffer.ToString());
        }
        catch
        {
            // ignore
        }

        Console.WriteLine(buffer.ToString());
        Environment.Exit(0);
    }
}
