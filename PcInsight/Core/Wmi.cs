using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace PcInsight.Core;

/// <summary>
/// Read-only WMI client. Queries go through the Windows Management Infrastructure API
/// (<c>Microsoft.Management.Infrastructure</c>) over DCOM, which is the supported way to read
/// CIM classes from modern .NET. It also keeps the app free of the obsolete
/// <c>System.Management</c> dependency.
/// </summary>
public static class Wmi
{
    public const string DefaultNamespace = @"root\cimv2";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

    // One session per calling thread: the collectors run in parallel on the thread pool and
    // CimSession usage is not guaranteed to be thread safe for parallel operations.
    private static readonly System.Threading.ThreadLocal<CimSession?> Session = new(() => null);

    /// <summary>Diagnostics: the last error seen by the transport, for troubleshooting.</summary>
    public static string LastError { get; private set; } = string.Empty;

    public static bool Available()
    {
        try
        {
            var session = GetSession();
            return session is not null && session.TestConnection();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static CimSession? GetSession()
    {
        if (Session.Value is { } existing)
        {
            return existing;
        }

        try
        {
            var options = new DComSessionOptions
            {
                Timeout = QueryTimeout,
            };

            // A null computer name targets the local machine.
            var created = CimSession.Create(null, options);
            Session.Value = created;
            return created;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    /// <summary>Run <paramref name="wql"/> and return every instance as a property bag.</summary>
    public static List<WmiRow> Query(string wql, string? ns = null)
    {
        var result = new List<WmiRow>();
        var session = GetSession();
        if (session is null)
        {
            return result;
        }

        var target = string.IsNullOrWhiteSpace(ns) ? DefaultNamespace : ns!;
        try
        {
            foreach (var instance in session.QueryInstances(target, "WQL", wql))
            {
                var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in instance.CimInstanceProperties)
                {
                    var text = ToText(property.Value);
                    if (!string.IsNullOrEmpty(text))
                    {
                        bag[property.Name] = text;
                    }
                }

                if (bag.Count > 0)
                {
                    result.Add(new WmiRow(bag));
                }
            }

            LastError = string.Empty;
        }
        catch (CimException ex)
        {
            // Access denied (elevation required) and unsupported classes are expected on many
            // machines; the caller simply receives no rows.
            LastError = ex.Message;
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + ": " + ex.Message;
        }

        return result;
    }

    public static WmiRow? First(string wql, string? ns = null)
    {
        var rows = Query(wql, ns);
        return rows.Count > 0 ? rows[0] : null;
    }

    public static bool HasClass(string className, string? ns = null)
    {
        try
        {
            return Query("SELECT * FROM " + className, ns).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>WMI values are already projected to .NET types; normalise them to text.</summary>
    private static string ToText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string s:
                return s;
            case bool b:
                return b ? "TRUE" : "FALSE";
            case string[] array:
                return string.Join(", ", array.Where(v => !string.IsNullOrWhiteSpace(v)));
            case DateTime dt:
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            case byte[] bytes:
                return BitConverter.ToString(bytes).Replace("-", string.Empty);
            case IFormattable f:
                return f.ToString(null, CultureInfo.InvariantCulture);
            default:
                return value.ToString() ?? string.Empty;
        }
    }
}

/// <summary>One WMI instance: a read-only property bag with typed accessors.</summary>
public sealed class WmiRow
{
    public static readonly WmiRow Empty = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, string> _bag;

    public WmiRow(Dictionary<string, string> bag)
    {
        _bag = bag;
    }

    public IReadOnlyDictionary<string, string> Properties => _bag;

    public string? Raw(string name) => _bag.TryGetValue(name, out var v) ? v : null;

    public string Str(string name, string fallback = "") => Fmt.Or(Raw(name), fallback);

    public string? StrOrNull(string name) => Fmt.Has(Raw(name)) ? Raw(name) : null;

    public ulong? U64(string name)
    {
        var raw = Raw(name);
        if (!Fmt.Has(raw))
        {
            return null;
        }

        if (ulong.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d >= 0)
        {
            return (ulong)d;
        }

        return null;
    }

    public long? I64(string name)
    {
        var v = U64(name);
        return v is null ? null : (long)v.Value;
    }

    public int? I32(string name)
    {
        var v = I64(name);
        return v is null ? null : (int)Math.Clamp(v.Value, int.MinValue, int.MaxValue);
    }

    public uint? U32(string name)
    {
        var v = U64(name);
        return v is null ? null : (uint)Math.Min(v.Value, uint.MaxValue);
    }

    public double? D(string name)
    {
        var raw = Raw(name);
        return Fmt.Has(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    public bool? B(string name)
    {
        var raw = Raw(name);
        if (!Fmt.Has(raw))
        {
            return null;
        }

        if (raw.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || raw == "1")
        {
            return true;
        }

        if (raw.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || raw == "0")
        {
            return false;
        }

        return null;
    }

    /// <summary>DMTF datetime ("20261012080000.000000+480") to a local DateTime.</summary>
    public DateTime? Date(string name)
    {
        var raw = Raw(name);
        if (!Fmt.Has(raw) || raw.Length < 8)
        {
            return null;
        }

        try
        {
            var year = int.Parse(raw.Substring(0, 4), CultureInfo.InvariantCulture);
            var month = int.Parse(raw.Substring(4, 2), CultureInfo.InvariantCulture);
            var day = int.Parse(raw.Substring(6, 2), CultureInfo.InvariantCulture);
            var hour = raw.Length >= 10 ? int.Parse(raw.Substring(8, 2), CultureInfo.InvariantCulture) : 0;
            var min = raw.Length >= 12 ? int.Parse(raw.Substring(10, 2), CultureInfo.InvariantCulture) : 0;
            var sec = raw.Length >= 14 ? int.Parse(raw.Substring(12, 2), CultureInfo.InvariantCulture) : 0;
            if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || min > 59 || sec > 59)
            {
                return null;
            }

            return new DateTime(year, month, day, hour, min, sec);
        }
        catch
        {
            return null;
        }
    }

    public string[] Array(string name)
    {
        var raw = Raw(name);
        if (!Fmt.Has(raw))
        {
            return System.Array.Empty<string>();
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
