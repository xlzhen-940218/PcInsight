using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PcInsight.Core;

/// <summary>Severity used to colour a value in the UI.</summary>
public enum Tone
{
    Normal,
    Good,
    Warn,
    Bad,
    Muted,
}

/// <summary>Overall health of a section (drives the little status dot).</summary>
public enum Condition
{
    Ok,
    Info,
    Warn,
    Bad,
}

/// <summary>A single label/value row.</summary>
public sealed class Item : INotifyPropertyChanged
{
    public Item(string label, string? value, string? detail = null, Tone tone = Tone.Normal)
    {
        Label = label;
        Store = value;
        DetailStore = detail;
        Tone = tone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Resource key or literal text for the label.</summary>
    public string Label { get; }

    public Tone Tone { get; set; }

    /// <summary>Language-independent value, used for export and copying.</summary>
    public string? Store { get; }

    public string? DetailStore { get; }

    public string DisplayLabel => Str.Resolve(Label, Babel.L.Current);

    public string DisplayValue => Store ?? Fmt.NotAvailable;

    public string DisplayDetail => DetailStore ?? string.Empty;

    public bool HasDetail => !string.IsNullOrWhiteSpace(DetailStore);

    /// <summary>Label plus optional parenthesised detail, used where only one text slot is available.</summary>
    public string DisplayLabelWithDetail => HasDetail ? DisplayLabel + " (" + DisplayDetail + ")" : DisplayLabel;

    /// <summary>Colour key resolved by the ToneBrushConverter in the UI layer.</summary>
    public Tone DisplayTone => Tone;

    /// <summary>Re-evaluate every computed property after a language switch.</summary>
    public void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabelWithDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayValue)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetail)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTone)));
    }
}

/// <summary>A titled group of items, e.g. one memory module or one network adapter.</summary>
public sealed class Group : INotifyPropertyChanged
{
    public Group(string title)
    {
        Title = title;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Subtitle { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public ObservableCollection<Item> Items { get; } = new();

    public string DisplayTitle => Str.Resolve(Title, Babel.L.Current);

    public string DisplaySubtitle => Subtitle;

    public bool IsEmpty => Items.Count == 0;

    public Group Add(string label, string? value, string? detail = null, Tone tone = Tone.Normal)
    {
        Items.Add(new Item(label, value, detail, tone));
        return this;
    }

    /// <summary>Add the row, but skip it when the value carries no information.</summary>
    public Group AddIf(string label, string? value, string? detail = null, Tone tone = Tone.Normal)
    {
        if (Fmt.Has(value))
        {
            Add(label, value, detail, tone);
        }

        return this;
    }

    public void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplaySubtitle)));
        foreach (var i in Items)
        {
            i.RaiseAll();
        }
    }
}

/// <summary>A tabular row (used for the long lists: software, services, processes ...).</summary>
public sealed class TableRow : INotifyPropertyChanged
{
    private readonly string?[] _store;

    public TableRow(params string?[] values)
    {
        _store = values;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string?[] Values => _store;

    public Tone Tone { get; set; }

    public string C0 => Get(0);
    public string C1 => Get(1);
    public string C2 => Get(2);
    public string C3 => Get(3);
    public string C4 => Get(4);
    public string C5 => Get(5);

    public string Get(int i) => i < _store.Length ? _store[i] ?? string.Empty : string.Empty;

    public void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C0)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C1)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C2)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C3)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C4)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(C5)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tone)));
    }
}

public sealed class TableColumn
{
    public TableColumn(string header, double width, bool right = false)
    {
        Header = header;
        Width = width;
        Right = right;
    }

    /// <summary>Resource key or literal text for the column header.</summary>
    public string Header { get; }

    /// <summary>Relative column width (star units).</summary>
    public double Width { get; }

    public bool Right { get; }

    public string DisplayHeader => Str.Resolve(Header, Babel.L.Current);

    public void RaiseAll()
    {
        // Column headers are re-read when the table is rebuilt after a language switch.
    }

    public override string ToString() => DisplayHeader;
}

/// <summary>One navigable section of the app, e.g. "Processor".</summary>
public sealed class Section : INotifyPropertyChanged
{
    public Section(string key, string title, string glyph)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// True while the collector for this section is still running. The shell seeds every section as
    /// loading so each page can be shown immediately and filled in as results arrive.
    /// </summary>
    public bool IsLoading { get; set; } = true;

    /// <summary>Resource keys shown as placeholder rows while the section has not been collected.</summary>
    public IReadOnlyList<string> PendingLabels { get; set; } = Array.Empty<string>();

    public string Key { get; }

    /// <summary>Resource key of the section title.</summary>
    public string Title { get; }

    public string Glyph { get; }

    public string Summary { get; set; } = string.Empty;

    public Condition Severity { get; set; } = Condition.Ok;

    public ObservableCollection<Group> Groups { get; } = new();

    public ObservableCollection<TableColumn> Columns { get; } = new();

    public ObservableCollection<TableRow> Rows { get; } = new();

    public bool HasTable => Rows.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    public bool IsEmpty => !HasTable && !HasGroups;
    public string DisplayTitle => Str.Resolve(Title, Babel.L.Current);

    public int ItemCount => Groups.Sum(g => g.Items.Count) + Rows.Count;

    public Group AddGroup(string title)
    {
        var g = new Group(title);
        Groups.Add(g);
        return g;
    }

    /// <summary>
    /// Replace this section's content with what a collector produced. The section object identity
    /// is preserved so the navigation item and the open page keep pointing at it.
    /// </summary>
    public void MergeFrom(Section finished)
    {
        Groups.Clear();
        Columns.Clear();
        Rows.Clear();

        foreach (var g in finished.Groups)
        {
            Groups.Add(g);
        }

        foreach (var c in finished.Columns)
        {
            Columns.Add(c);
        }

        foreach (var r in finished.Rows)
        {
            Rows.Add(r);
        }

        Summary = finished.Summary;
        Severity = finished.Severity;
        IsLoading = false;
        PendingLabels = Array.Empty<string>();

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTable)));
    }

    public void AddRow(Tone tone, params string?[] cells) => Rows.Add(new TableRow(cells) { Tone = tone });

    public void RaiseAll()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        foreach (var g in Groups)
        {
            g.RaiseAll();
        }

        foreach (var r in Rows)
        {
            r.RaiseAll();
        }
    }
}

/// <summary>Complete scan result for the machine.</summary>
public sealed class Snapshot
{
    public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.Now;

    public TimeSpan Duration { get; set; }

    public List<Section> Sections { get; } = new();

    public List<Section> Hardware { get; } = new();

    public List<Section> Software { get; } = new();

    /// <summary>Scanner diagnostics (for example the error text of a collector that failed).</summary>
    public Dictionary<string, string> Raw { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Section? Find(string key) => Sections.FirstOrDefault(s => s.Key == key);

    public void RaiseAll()
    {
        foreach (var s in Sections)
        {
            s.RaiseAll();
        }
    }

    public string ToPlainText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("PC Insight — " + GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        foreach (var s in Sections)
        {
            if (s.IsEmpty)
            {
                continue;
            }

            sb.AppendLine();
            sb.AppendLine("== " + s.DisplayTitle + " ==");
            foreach (var g in s.Groups)
            {
                if (g.Items.Count == 0)
                {
                    continue;
                }

                sb.AppendLine("-- " + g.DisplayTitle);
                foreach (var i in g.Items)
                {
                    sb.AppendLine("  " + i.DisplayLabel + ": " + i.Store);
                }
            }

            if (s.HasTable)
            {
                sb.AppendLine("  " + string.Join(" | ", s.Columns.Select(c => c.DisplayHeader)));
                foreach (var r in s.Rows)
                {
                    sb.AppendLine("  " + string.Join(" | ", r.Values.Select(v => v ?? string.Empty)));
                }
            }
        }

        return sb.ToString();
    }
}
