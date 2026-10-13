using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PcInsight.Core;

/// <summary>Version resource of an executable / driver file.</summary>
public sealed record FileInfoResult(
    string? Description,
    string? Company,
    string? Product,
    string? Version,
    string? FileVersion,
    string? Copyright,
    long? Size,
    DateTime? Modified,
    string? OriginalName)
{
    public static readonly FileInfoResult Empty = new(null, null, null, null, null, null, null, null, null);

    public static FileInfoResult Read(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Empty;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!File.Exists(expanded))
            {
                return Empty;
            }

            var fi = new System.IO.FileInfo(expanded);
            var vi = FileVersionInfo.GetVersionInfo(expanded);
            return new FileInfoResult(
                Clean(vi.FileDescription),
                Clean(vi.CompanyName),
                Clean(vi.ProductName),
                Clean(vi.ProductVersion),
                Clean(vi.FileVersion),
                Clean(vi.LegalCopyright),
                SafeLength(fi),
                SafeModified(fi),
                Clean(vi.OriginalFilename));
        }
        catch
        {
            return Empty;
        }
    }

    private static long? SafeLength(System.IO.FileInfo fi)
    {
        try
        {
            return fi.Length;
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? SafeModified(System.IO.FileInfo fi)
    {
        try
        {
            return fi.LastWriteTime;
        }
        catch
        {
            return null;
        }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Authenticode signature state of a driver or executable.</summary>
public static class Signature
{
    public enum State
    {
        Unknown,
        Signed,
        SignedTrusted,
        Unsigned,
    }

    public static (State State, string? Signer) Of(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return (State.Unknown, null);
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!File.Exists(expanded))
            {
                return (State.Unknown, null);
            }

            // X509Certificate.CreateFromSignedFile throws when the file carries no signature.
            var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(expanded);
            var name = cert.Subject;
            var cn = ExtractCommonName(name);
            return (State.SignedTrusted, cn);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return (State.Unsigned, null);
        }
        catch
        {
            return (State.Unknown, null);
        }
    }

    private static string? ExtractCommonName(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        foreach (var part in subject!.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return p[3..].Trim();
            }
        }

        return subject;
    }
}

/// <summary>Read-only registry helpers (safe on any key).</summary>
public static class Reg
{
    public static string? Value(Microsoft.Win32.RegistryHive hive, string subKey, string name, bool view32 = false, bool view64 = false)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                hive,
                view32 ? Microsoft.Win32.RegistryView.Registry32
                : view64 ? Microsoft.Win32.RegistryView.Registry64
                : Microsoft.Win32.RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, false);
            var value = key?.GetValue(name);
            if (value is null)
            {
                return null;
            }

            return value switch
            {
                string s => s,
                string[] a => string.Join(", ", a),
                byte[] bytes => BitConverter.ToString(bytes),
                _ => value.ToString(),
            };
        }
        catch
        {
            return null;
        }
    }

    public static long? Dword(Microsoft.Win32.RegistryHive hive, string subKey, string name)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, false);
            var value = key?.GetValue(name);
            return value is null ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static List<(string Name, string Value)> Values(Microsoft.Win32.RegistryHive hive, string subKey)
    {
        var list = new List<(string, string)>();
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, false);
            if (key is null)
            {
                return list;
            }

            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name);
                list.Add((name, value switch
                {
                    null => string.Empty,
                    string s => s,
                    string[] a => string.Join(", ", a),
                    _ => value.ToString() ?? string.Empty,
                }));
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    public static List<string> SubKeys(Microsoft.Win32.RegistryHive hive, string subKey, bool view64 = false)
    {
        var list = new List<string>();
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(
                hive,
                view64 ? Microsoft.Win32.RegistryView.Registry64 : Microsoft.Win32.RegistryView.Default);
            using var key = baseKey.OpenSubKey(subKey, false);
            if (key is null)
            {
                return list;
            }

            list.AddRange(key.GetSubKeyNames());
        }
        catch
        {
            // ignore
        }

        return list;
    }
}
