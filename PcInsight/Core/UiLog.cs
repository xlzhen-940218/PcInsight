using System;
using System.IO;
using System.Text;

namespace PcInsight.Core;

/// <summary>
/// Diagnostic log. Written to <c>%TEMP%\PcInsight-log.txt</c> so that a crash in the WinUI layer
/// still leaves a trace of how far startup and rendering got. Off unless <see cref="Enabled"/>.
/// </summary>
public static class UiLog
{
    private static readonly object Gate = new();
    private static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PcInsight-log.txt");

    public static string FilePath => Path;

    /// <summary>
    /// Off by default so normal runs do not write a log file. The <c>--uicheck</c> switch turns it
    /// on for diagnostics.
    /// </summary>
    public static bool Enabled { get; set; }

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never break the app
        }
    }

    public static void Reset()
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.WriteAllText(Path, DateTime.Now.ToString("s") + "  --- session start ---" + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static void WriteSection(string name)
    {
        Write("=== " + name + " ===");
    }
}

/// <summary>
/// Locates the folder the application was actually launched from. Under a single-file publish
/// <see cref="AppContext.BaseDirectory"/> points at the extraction directory in %TEMP%, so the
/// real executable path is preferred for anything the user is meant to find.
/// </summary>
public static class AppPaths
{
    public static string ExecutableDirectory
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                {
                    var dir = Path.GetDirectoryName(exe);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        return dir!;
                    }
                }
            }
            catch
            {
                // fall back below
            }

            return AppContext.BaseDirectory;
        }
    }

    /// <summary>True when running from a single-file bundle (extraction directory in %TEMP%).</summary>
    public static bool IsSingleFileBundle
    {
        get
        {
            try
            {
                var exeDir = ExecutableDirectory;
                var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                return !string.Equals(exeDir.TrimEnd(Path.DirectorySeparatorChar), baseDir, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
