using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PcInsight.Core;

/// <summary>
/// Value formatting helpers. All UI text that ends up in an <c>Item</c> is produced here so the
/// snapshot stays language independent.
/// </summary>
public static class Fmt
{
    public static string Unknown => Babel.L["label.unknown"];

    public static string NotAvailable => Babel.L["label.notAvailable"];

    public static string None => Babel.L["label.none"];

    public static string Yes => Babel.L["label.yes"];

    public static string No => Babel.L["label.no"];

    public static bool Has(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        // Tolerate the localized placeholders: collectors normally store null for "no data",
        // but a few paths pass through already-formatted text.
        return !string.Equals(s, Babel.L["label.unknown"], StringComparison.Ordinal)
            && !string.Equals(s, Babel.L["label.notAvailable"], StringComparison.Ordinal);
    }

    public static string Or(string? value, string fallback) => Has(value) ? value!.Trim() : fallback;

    public static string Txt(string? value) => Has(value) ? value!.Trim() : Unknown;

    public static string B(bool? v) => v is null ? Unknown : v.Value ? Yes : No;

    public static string B(string? v) => Str(v);

    /// <summary>Map a WMI boolean-ish string ("TRUE"/"FALSE"/"1") to a localized yes/no.</summary>
    public static string Str(string? v)
    {
        if (!Has(v))
        {
            return Unknown;
        }

        var s = v!.Trim();
        if (s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1")
        {
            return Yes;
        }

        if (s.Equals("FALSE", StringComparison.OrdinalIgnoreCase) || s == "0")
        {
            return No;
        }

        return s;
    }

    public static string Num(double? v, int decimals = 0, string suffix = "")
    {
        if (v is null)
        {
            return Unknown;
        }

        return v.Value.ToString("N" + decimals, CultureInfo.CurrentCulture) + suffix;
    }

    public static string Int(long? v) => v is null ? Unknown : v.Value.ToString("N0", CultureInfo.CurrentCulture);

    public static string Size(double? bytes, int decimals = 1)
    {
        if (bytes is null)
        {
            return Unknown;
        }

        var b = bytes.Value;
        if (b <= 0)
        {
            return Unknown;
        }

        string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
        var i = 0;
        while (b >= 1024 && i < units.Length - 1)
        {
            b /= 1024;
            i++;
        }

        return b.ToString("N" + decimals, CultureInfo.CurrentCulture) + " " + units[i];
    }

    public static string Mhz(uint? mhz) => mhz is null or 0 ? Unknown : mhz.Value.ToString("N0", CultureInfo.CurrentCulture) + " MHz";

    public static string Ghz(uint? mhz) => mhz is null or 0 ? Unknown : (mhz.Value / 1000.0).ToString("N2", CultureInfo.CurrentCulture) + " GHz";

    public static string Pct(double? v, int decimals = 0) =>
        v is null ? Unknown : v.Value.ToString("N" + decimals, CultureInfo.CurrentCulture) + " %";

    public static string Seconds(ulong? s)
    {
        if (s is null or 0)
        {
            return Unknown;
        }

        return Duration(TimeSpan.FromSeconds(s.Value));
    }

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            return Unknown;
        }

        var parts = new List<string>();
        if (t.Days > 0)
        {
            parts.Add(t.Days + (Babel.L.IsChinese ? " 天" : " d"));
        }

        if (t.Hours > 0)
        {
            parts.Add(t.Hours + (Babel.L.IsChinese ? " 小时" : " h"));
        }

        parts.Add(t.Minutes + (Babel.L.IsChinese ? " 分" : " min"));
        return string.Join(" ", parts);
    }

    public static string Date(string? dmtf)
    {
        if (!Has(dmtf))
        {
            return Unknown;
        }

        var s = dmtf!.Trim();
        if (s.Length < 8)
        {
            return s;
        }

        try
        {
            var year = int.Parse(s.Substring(0, 4), CultureInfo.InvariantCulture);
            var month = int.Parse(s.Substring(4, 2), CultureInfo.InvariantCulture);
            var day = int.Parse(s.Substring(6, 2), CultureInfo.InvariantCulture);
            var hour = s.Length >= 10 ? int.Parse(s.Substring(8, 2), CultureInfo.InvariantCulture) : 0;
            var min = s.Length >= 12 ? int.Parse(s.Substring(10, 2), CultureInfo.InvariantCulture) : 0;
            var sec = s.Length >= 14 ? int.Parse(s.Substring(12, 2), CultureInfo.InvariantCulture) : 0;
            if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || min > 59 || sec > 59)
            {
                return s;
            }

            var dt = new DateTime(year, month, day, hour, min, sec, DateTimeKind.Unspecified);
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch
        {
            return s;
        }
    }

    public static string DateOnly(string? dmtf) => Date(dmtf) is var d && d.Length >= 10 ? d[..10] : d;

    public static string DateTimeText(DateTime? dt) =>
        dt is null ? Unknown : dt.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public static string DateText(DateTime? dt) =>
        dt is null ? Unknown : dt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string RegistryDate(string? value)
    {
        if (!Has(value))
        {
            return Unknown;
        }

        var s = value!.Trim();
        if (s.Length == 8 && s.All(char.IsDigit))
        {
            return s[..4] + "-" + s.Substring(4, 2) + "-" + s.Substring(6, 2);
        }

        return s;
    }

    /// <summary>
    /// Windows edition. The registry ProductName still says "Windows 10" on Windows 11, so the
    /// build number decides the marketing name and the EditionID decides the SKU.
    /// </summary>
    public static string Edition(string? caption, string? editionId, int? build = null)
    {
        var zh = Babel.L.IsChinese;
        var windows11 = build is >= 22000;
        var prefix = windows11 ? "Windows 11" : "Windows 10";

        var id = editionId ?? string.Empty;
        if (id.Contains("ProfessionalWorkstation", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + " " + (zh ? "专业工作站版" : "Pro for Workstations");
        }

        if (id.Contains("Professional", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + " " + (zh ? "专业版" : "Pro");
        }

        if (id.Contains("Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + " " + (zh ? "企业版" : "Enterprise");
        }

        if (id.Contains("Education", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + " " + (zh ? "教育版" : "Education");
        }

        if (id.Contains("Core", StringComparison.OrdinalIgnoreCase) || id.Contains("Home", StringComparison.OrdinalIgnoreCase))
        {
            return prefix + " " + (zh ? "家庭版" : "Home");
        }

        if (Fmt.Has(id))
        {
            return prefix + " " + id;
        }

        // Fall back to the caption when the registry has no edition id.
        var c = caption ?? string.Empty;
        if (Fmt.Has(c))
        {
            var normalized = c.Replace("Windows 10", prefix, StringComparison.OrdinalIgnoreCase);
            return normalized;
        }

        return Unknown;
    }

    public static string Join(IEnumerable<string>? values, string separator = ", ")
    {
        if (values is null)
        {
            return string.Empty;
        }

        var list = values.Where(Has).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? string.Empty : string.Join(separator, list);
    }

    public static string BytesToGbString(ulong? bytes, int decimals = 1) =>
        bytes is null ? Unknown : Size(bytes.Value, decimals);

    public static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        return s!.Length <= max ? s : s[..(max - 1)] + "…";
    }
}

/// <summary>
/// JSON exporter. The document is written directly rather than through <c>System.Text.Json</c>'s
/// serializer: the release build is trimmed, which disables reflection-based metadata, and hand
/// writing keeps both the trimmed size and the runtime behaviour predictable.
/// </summary>
public static class Export
{
    public static string ToJson(Snapshot snap, bool pretty = true)
    {
        var writer = new JsonWriter(pretty);
        writer.StartObject();
        writer.Property("generatedAt", snap.GeneratedAt.ToString("o", CultureInfo.InvariantCulture));
        writer.NumberProperty("scanSeconds", Math.Round(snap.Duration.TotalSeconds, 2));

        writer.PropertyName("sections");
        writer.StartArray();
        foreach (var s in snap.Sections.Where(s => !s.IsEmpty))
        {
            writer.StartObject();
            writer.Property("key", s.Key);
            writer.Property("title", s.DisplayTitle);

            writer.PropertyName("groups");
            writer.StartArray();
            foreach (var g in s.Groups.Where(g => g.Items.Count > 0))
            {
                writer.StartObject();
                writer.Property("title", g.DisplayTitle);

                writer.PropertyName("items");
                writer.StartObject();
                foreach (var i in g.Items.Where(i => Fmt.Has(i.Store)))
                {
                    // Two items can share a label (for example f.memDataWidth across modules);
                    // those become an array so no value is lost.
                    var duplicates = g.Items
                        .Where(x => x.DisplayLabel == i.DisplayLabel && Fmt.Has(x.Store))
                        .Select(x => x.Store)
                        .ToList();

                    if (duplicates.Count > 1)
                    {
                        if (!writer.HasEmitted(i.DisplayLabel))
                        {
                            writer.PropertyName(i.DisplayLabel);
                            writer.StartArray();
                            foreach (var value in duplicates)
                            {
                                writer.Value(value);
                            }

                            writer.EndArray();
                        }
                    }
                    else
                    {
                        writer.Property(i.DisplayLabel, i.Store);
                    }
                }

                writer.EndObject();
                writer.EndObject();
            }

            writer.EndArray();

            writer.PropertyName("columns");
            writer.StartArray();
            foreach (var c in s.Columns)
            {
                writer.Value(c.DisplayHeader);
            }

            writer.EndArray();

            writer.PropertyName("rows");
            writer.StartArray();
            foreach (var r in s.Rows)
            {
                writer.StartArray();
                foreach (var v in r.Values)
                {
                    writer.Value(v);
                }

                writer.EndArray();
            }

            writer.EndArray();

            writer.EndObject();
        }

        writer.EndArray();
        writer.EndObject();
        return writer.ToString();
    }
}

/// <summary>
/// Minimal streaming JSON writer with correct string escaping. Indentation is added when
/// <c>pretty</c> is set, which is only used for the human-readable export.
/// </summary>
internal sealed class JsonWriter
{
    private readonly StringBuilder _sb = new();
    private readonly bool _pretty;
    private readonly HashSet<string> _emittedKeys = new(StringComparer.Ordinal);

    /// <summary>Last significant character written: '{', '[' or ':' means a value is expected.</summary>
    private char _last = '\0';

    private int _indent;

    public JsonWriter(bool pretty)
    {
        _pretty = pretty;
    }

    public void StartObject()
    {
        PrepareValue();
        _sb.Append('{');
        _last = '{';
        _emittedKeys.Clear();
    }

    public void EndObject()
    {
        if (_last != '{')
        {
            NewLine(_indent);
        }

        _sb.Append('}');
        _last = '}';
    }

    public void StartArray()
    {
        PrepareValue();
        _sb.Append('[');
        _last = '[';
    }

    public void EndArray()
    {
        if (_last != '[')
        {
            NewLine(_indent);
        }

        _sb.Append(']');
        _last = ']';
    }

    public void Property(string name, string? value)
    {
        PropertyName(name);
        Value(value);
    }

    public void NumberProperty(string name, double value)
    {
        PropertyName(name);
        _sb.Append(value.ToString("R", CultureInfo.InvariantCulture));
        _last = '0';
    }

    public void PropertyName(string name)
    {
        PrepareValue();
        NewLine(_indent + 1);
        _sb.Append('"').Append(Escape(name)).Append("\":");
        if (_pretty)
        {
            _sb.Append(' ');
        }

        _last = ':';
        _emittedKeys.Add(name);
    }

    public void Value(string? value)
    {
        PrepareValue();
        if (value is null)
        {
            _sb.Append("null");
            _last = 'n';
            return;
        }

        _sb.Append('"').Append(Escape(value)).Append('"');
        _last = '"';
    }

    public bool HasEmitted(string name) => _emittedKeys.Contains(name);

    public override string ToString() => _sb.ToString();

    private void PrepareValue()
    {
        // A value is expected right after '{', '[' or ':', so no separator is written there.
        if (_last is '{' or '[' or ':' or '\0')
        {
            return;
        }

        _sb.Append(',');
        if (_pretty)
        {
            _sb.Append(' ');
        }
    }

    private void NewLine(int depth)
    {
        if (!_pretty)
        {
            return;
        }

        _sb.Append('\n');
        _sb.Append(' ', depth * 2);
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }
}


