using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PcInsight.Core;

public enum Lang
{
    Zh,
    En,
}

public static class Str
{
    public const string KeyPrefix = "k|";

    /// <summary>Resolve a string-table key for the given language. Unknown keys resolve to the key itself.</summary>
    public static string Resolve(string key, Lang lang)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (!Strings.Table.TryGetValue(key, out var v))
        {
            return key;
        }

        return lang == Lang.En ? v[1] : v[0];
    }

    public static string Format(string key, Lang lang, params object?[] args)
    {
        var text = Resolve(key, lang);
        if (args.Length == 0)
        {
            return text;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            return text;
        }
    }
}

/// <summary>
/// Localization / language state. Exposed to XAML through <c>{Binding L[...]}</c> (indexer binding),
/// so a language switch refreshes every bound label immediately.
/// </summary>
public sealed class Babel : INotifyPropertyChanged
{
    /// <summary>
    /// Process-wide singleton. The nested holder gives the CLR a single static slot for the
    /// instance, so worker threads observe exactly the same object (and language) as the UI
    /// thread — a <c>ThreadStatic</c>-shaped lazy field would create one copy per thread.
    /// </summary>
    private static class Holder
    {
        internal static readonly Babel Value = new();
    }

    public static Babel Instance => Holder.Value;

    private const string PrefKey = "language";
    private static readonly string PrefFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PcInsight",
        "settings.json");

    private Lang _lang;

    private Babel()
    {
        _lang = Detect();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public static Babel L => Instance;

    public Lang Current
    {
        get => _lang;
        set
        {
            if (_lang == value)
            {
                return;
            }

            _lang = value;
            Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCode)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChinese)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLabel)));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsChinese
    {
        get => _lang == Lang.Zh;
        set => Current = value ? Lang.Zh : Lang.En;
    }

    public string CurrentCode => _lang == Lang.En ? "en" : "zh";

    public string CurrentLabel => _lang == Lang.En ? "EN" : "中文";

    /// <summary>Indexer used by XAML: <c>{Binding L[action.refresh]}</c>.</summary>
    public string this[string key] => Str.Resolve(key, _lang);

    public string F(string key, params object?[] args) => Str.Format(key, _lang, args);

    public void Toggle() => Current = _lang == Lang.Zh ? Lang.En : Lang.Zh;

    private static Lang Detect()
    {
        try
        {
            if (File.Exists(PrefFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(PrefFile));
                if (doc.RootElement.TryGetProperty(PrefKey, out var v))
                {
                    var s = v.GetString();
                    if (string.Equals(s, "en", StringComparison.OrdinalIgnoreCase))
                    {
                        return Lang.En;
                    }

                    if (string.Equals(s, "zh", StringComparison.OrdinalIgnoreCase))
                    {
                        return Lang.Zh;
                    }
                }
            }
        }
        catch
        {
            // fall through to OS detection
        }

        var ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.Equals(ui, "zh", StringComparison.OrdinalIgnoreCase) ? Lang.Zh : Lang.En;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefFile)!);
            File.WriteAllText(PrefFile, JsonSerializer.Serialize(new Dictionary<string, string> { [PrefKey] = CurrentCode }));
        }
        catch
        {
            // preferences are best-effort
        }
    }
}
