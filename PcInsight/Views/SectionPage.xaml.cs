using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PcInsight.Core;

namespace PcInsight.Views;

/// <summary>
/// Renders one <see cref="Section"/>: the overview hero cards plus, for every group,
/// a card containing label/value rows or a table.
/// </summary>
public sealed partial class SectionPage : UserControl
{
    private const double ColumnBreakpoint = 1080;

    private readonly List<string> _groupTitles = new();
    private ProgressRing? _cpuRing;
    private ProgressRing? _memRing;
    private TextBlock? _cpuText;
    private TextBlock? _memText;
    private TextBlock? _uptimeText;

    private Section? _section;
    private string _filter = string.Empty;
    private bool _lastWide;

    public SectionPage()
    {
        InitializeComponent();
        // Re-lay-out only when the single/two column breakpoint is crossed; rebuilding on every
        // size change churns the visual tree during layout.
        SizeChanged += (_, _) =>
        {
            var wide = ActualWidth > ColumnBreakpoint;
            if (wide != _lastWide)
            {
                _lastWide = wide;
                Rebuild();
            }
        };
        Babel.L.LanguageChanged += (_, _) => RefreshLanguage();
    }

    public bool ShowOverviewCards { get; set; }

    /// <summary>Width of the card host, for layout diagnostics.</summary>
    public double CardsWidth => CardsPanel.ActualWidth;

    public string Filter
    {
        get => _filter;
        set
        {
            var next = value ?? string.Empty;
            if (next == _filter)
            {
                return;
            }

            _filter = next;
            Rebuild();
        }
    }

    public Section? Section
    {
        get => _section;
        set
        {
            _section = value;
            _groupTitles.Clear();
            Rebuild();
        }
    }

    /// <summary>Called after a language switch: every card is rebuilt with the new strings.</summary>
    public void RefreshLanguage()
    {
        _groupTitles.Clear();
        Rebuild();
    }

    /// <summary>Called when the page's section received its data from a collector.</summary>
    public void RefreshData()
    {
        _groupTitles.Clear();
        Rebuild();
    }

    public void ShowMessage(string message)
    {
        CardsPanel.Children.Clear();
        EmptyPanel.Visibility = Visibility.Collapsed;
        CardsPanel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(16, 20, 16, 0),
            Foreground = ThemeBrush("ToneBadBrush"),
        });
    }

    /// <summary>Live values for the overview rings, sampled on a timer.</summary>
    public void UpdateLive(double? cpu, ulong? usedMemory, ulong? totalMemory, ulong? uptimeSeconds)
    {
        if (_cpuRing is null)
        {
            return;
        }

        if (cpu is not null)
        {
            _cpuRing.Value = cpu.Value;
            if (_cpuText is not null)
            {
                _cpuText.Text = Fmt.Pct(cpu, 0);
            }
        }

        if (usedMemory is not null && totalMemory is > 0)
        {
            var pct = usedMemory.Value * 100.0 / totalMemory!.Value;
            _memRing!.Value = pct;
            if (_memText is not null)
            {
                _memText.Text = Fmt.Size(usedMemory) + " / " + Fmt.Size(totalMemory);
            }
        }

        if (_uptimeText is not null && uptimeSeconds is not null)
        {
            _uptimeText.Text = Babel.L["f.uptime"] + ": " + Fmt.Duration(TimeSpan.FromSeconds(uptimeSeconds.Value));
        }
    }

    // ==================================================================== building
    private void Rebuild()
    {
        try
        {
            RebuildCore();
        }
        catch (Exception ex)
        {
            UiLog.Write("REBUILD FAILED: " + ex);
            CardsPanel.Children.Clear();
            EmptyPanel.Visibility = Visibility.Collapsed;
            CardsPanel.Children.Add(new TextBlock
            {
                Text = ex.ToString(),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Margin = new Thickness(16, 20, 16, 0),
                Foreground = ThemeBrush("ToneBadBrush"),
            });
        }
    }

    private void RebuildCore()
    {
        CardsPanel.Children.Clear();
        _cpuRing = _memRing = null;
        _cpuText = _memText = _uptimeText = null;

        if (_section is null)
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var wide = ActualWidth > ColumnBreakpoint;
        UiLog.Write("rebuild " + _section.Key + " width=" + (int)ActualWidth + " wide=" + wide + " groups=" + _section.Groups.Count);

        // While the collector for this page is still running, show a spinner on its own so the
        // shell is immediately readable instead of blank.
        if (_section.IsLoading)
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            CardsPanel.Children.Add(BuildLoadingCard());
            return;
        }

        if (ShowOverviewCards)
        {
            UiLog.Write("  overview header ...");
            BuildOverviewHeader();
            UiLog.Write("  overview header done");
        }
        var groups = _section.Groups
            .Where(g => !g.IsEmpty)
            .Where(Matches)
            .ToList();

        UiLog.Write("  visible groups=" + groups.Count);

        if (wide && groups.Count > 1)
        {
            for (var i = 0; i < groups.Count; i += 2)
            {
                var first = BuildGroupCard(groups[i]);
                if (i + 1 < groups.Count)
                {
                    var second = BuildGroupCard(groups[i + 1]);
                    CardsPanel.Children.Add(TwoColumns(first, second));
                }
                else
                {
                    CardsPanel.Children.Add(TwoColumns(first, null));
                }

                UiLog.Write("  cards " + i + "/" + groups.Count);
            }
        }
        else
        {
            foreach (var g in groups)
            {
                CardsPanel.Children.Add(BuildGroupCard(g));
            }

            UiLog.Write("  single column cards done");
        }

        if (_section.HasTable && (_filter.Length == 0 || FilteredRows().Count > 0))
        {
            UiLog.Write("  building table, rows=" + _section.Rows.Count + " cols=" + _section.Columns.Count);
            CardsPanel.Children.Add(BuildTable());
            UiLog.Write("  table done");
        }

        var empty = _section.IsEmpty || (groups.Count == 0 && (!_section.HasTable || FilteredRows().Count == 0));
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<TableRow> FilteredRows() => _section is null
        ? new List<TableRow>()
        : _section.Rows.Where(r => MatchesRow(r)).ToList();

    private bool Matches(Group g)
    {
        if (_filter.Length == 0)
        {
            return true;
        }

        if (g.DisplayTitle.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
            || g.Subtitle.Contains(_filter, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        return g.Items.Any(i =>
            i.DisplayLabel.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
            || (i.Store ?? string.Empty).Contains(_filter, StringComparison.CurrentCultureIgnoreCase));
    }

    private bool MatchesRow(TableRow row)
    {
        if (_filter.Length == 0)
        {
            return true;
        }

        return row.Values.Any(v => (v ?? string.Empty).Contains(_filter, StringComparison.CurrentCultureIgnoreCase));
    }

    // ---------------------------------------------------------------- overview hero
    /// <summary>Spinner card shown while this page's collector is still running.</summary>
    private FrameworkElement BuildLoadingCard()
    {
        var card = NewCard();
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };

        row.Children.Add(new ProgressRing
        {
            IsActive = true,
            IsIndeterminate = true,
            Width = 26,
            Height = 26,
        });

        row.Children.Add(new TextBlock
        {
            Text = Babel.L["status.loading"],
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("SubtleTextBrush"),
        });

        card.Child = row;
        return card;
    }

    private void BuildOverviewHeader()
    {
        // headline: computer name / manufacturer / model / OS
        var computer = _section!.Groups.FirstOrDefault(g => g.Title == "grp.computer");
        var os = _section.Groups.FirstOrDefault(g => g.Title == "grp.os");

        string? Value(Group? g, string label) => g?.Items.FirstOrDefault(i => i.Label == label)?.Store;

        var machine = string.Join(" · ", new[]
        {
            Value(computer, "f.computerName"),
            Value(computer, "f.manufacturer"),
            Value(computer, "f.model"),
        }.Where(Fmt.Has));

        var osText = string.Join(" · ", new[]
        {
            Value(os, "f.osName"),
            Value(os, "f.displayVersion") is { } dv && Fmt.Has(dv) ? (Babel.L.IsChinese ? "版本 " : "version ") + dv : null,
            Value(os, "f.osBuild") is { } b && Fmt.Has(b) ? "Build " + b : null,
            Value(os, "f.osArch"),
        }.Where(Fmt.Has));

        var heroText = new StackPanel { Spacing = 4 };
        heroText.Children.Add(new TextBlock
        {
            Text = Fmt.Or(machine, Environment.MachineName),
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        heroText.Children.Add(new TextBlock
        {
            Text = osText,
            FontSize = 13,
            Foreground = ThemeBrush("SubtleTextBrush"),
            TextWrapping = TextWrapping.Wrap,
        });

        var uptime = Value(os, "f.uptime");
        _uptimeText = new TextBlock
        {
            Text = (Babel.L["f.uptime"] + ": " + uptime).TrimEnd(':', ' '),
            FontSize = 12,
            Foreground = ThemeBrush("SubtleTextBrush"),
        };
        heroText.Children.Add(_uptimeText);

        // rings
        var (cpuRing, cpuText) = BuildRing(Babel.L["f.cpuLoad"], "CPU");
        var (memRing, memText) = BuildRing(Babel.L["f.memUsage"], "RAM");
        _cpuRing = cpuRing;
        _cpuText = cpuText;
        _memRing = memRing;
        _memText = memText;

        var rings = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22 };
        rings.Children.Add(RingBlock(cpuRing, cpuText));
        rings.Children.Add(RingBlock(memRing, memText));

        var osGroup = os;
        var memGroup = _section.Groups.FirstOrDefault(g => g.Title == "grp.memorySummary");
        var cpuGroup = _section.Groups.FirstOrDefault(g => g.Title == "grp.cpu");
        var storageGroup = _section.Groups.FirstOrDefault(g => (g.Subtitle ?? string.Empty).Contains(":\\", StringComparison.Ordinal));

        var facts = new StackPanel { Spacing = 6 };
        AddFact(facts, "f.cpuName", Value(cpuGroup, "f.cpuName"));
        AddFact(facts, "f.cpuCores", JoinCoreThreads(cpuGroup));
        AddFact(facts, "f.memTotal", Value(memGroup, "f.memTotal"));
        if (storageGroup is not null)
        {
            AddFact(facts, "f.volumeTotal", (storageGroup.Subtitle ?? string.Empty).Trim() + "   " + Value(storageGroup, "f.volumeTotal")
                + "   (" + (Babel.L.IsChinese ? "鍙敤 " : "free ") + Value(storageGroup, "f.volumeFree") + ")");
        }

        var layout = new Grid { ColumnSpacing = 24 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(heroText, 0);
        layout.Children.Add(heroText);

        var rightStack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        rightStack.Children.Add(rings);
        rightStack.Children.Add(facts);
        Grid.SetColumn(rightStack, 1);
        layout.Children.Add(rightStack);

        CardsPanel.Children.Add(new Border
        {
            Background = ThemeBrush("HeroBackgroundBrush"),
            BorderBrush = ThemeBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 18, 20, 16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = layout,
        });
    }

    private static string? JoinCoreThreads(Group? cpuGroup)
    {
        var cores = cpuGroup?.Items.FirstOrDefault(i => i.Label == "f.cpuCores")?.Store;
        var threads = cpuGroup?.Items.FirstOrDefault(i => i.Label == "f.cpuThreads")?.Store;
        if (!Fmt.Has(cores) && !Fmt.Has(threads))
        {
            return null;
        }

        return string.Join(" / ", new[] { cores, threads }.Where(Fmt.Has));
    }

    private void AddFact(Panel panel, string labelKey, string? value)
    {
        if (!Fmt.Has(value))
        {
            return;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(new TextBlock
        {
            Text = Babel.L[labelKey],
            FontSize = 12,
            Foreground = ThemeBrush("SubtleTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(row);
    }

    private static (ProgressRing Ring, TextBlock Text) BuildRing(string label, string shortLabel)
    {
        var ring = new ProgressRing
        {
            IsIndeterminate = false,
            Width = 62,
            Height = 62,
            Foreground = ThemeBrush("AccentFillColorDefaultBrush") is var accent && accent is SolidColorBrush c && c.Color == Microsoft.UI.Colors.Gray
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 95, 184))
                : accent,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };

        var text = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        return (ring, text);
    }

    private static StackPanel RingBlock(ProgressRing ring, TextBlock valueText)
    {
        var stack = new StackPanel { Spacing = 2, Width = 92, HorizontalAlignment = HorizontalAlignment.Center };
        var grid = new Grid();
        grid.Children.Add(ring);
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        inner.Children.Add(valueText);
        grid.Children.Add(inner);
        stack.Children.Add(grid);
        return stack;
    }

    // ---------------------------------------------------------------- group cards
    private FrameworkElement BuildGroupCard(Group group)
    {
        var card = NewCard();
        var content = new StackPanel { Spacing = 10 };

        var header = new StackPanel { Spacing = 1 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = group.DisplayTitle,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        titleRow.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = GroupTone(group),
        });
        header.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(group.Subtitle))
        {
            header.Children.Add(new TextBlock
            {
                Text = group.Subtitle,
                FontSize = 12,
                Foreground = ThemeBrush("SubtleTextBrush"),
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
        }

        content.Children.Add(header);
        content.Children.Add(new Rectangle { Height = 1, Fill = ThemeBrush("CardBorderBrush") });

        var items = group.Items.Where(i => MatchesItem(i)).ToList();
        content.Children.Add(CleanList(items, ItemTemplate()));

        card.Child = content;
        return card;
    }

    private bool MatchesItem(Item i) => _filter.Length == 0
        || i.DisplayLabel.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
        || (i.Store ?? string.Empty).Contains(_filter, StringComparison.CurrentCultureIgnoreCase);

    private static DataTemplate ItemTemplate()
    {
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            """
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Padding="0,4" ColumnSpacing="14">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="0.85*" />
                  <ColumnDefinition Width="1.6*" />
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0"
                           Text="{Binding DisplayLabelWithDetail}"
                           FontSize="13"
                           TextWrapping="Wrap"
                           IsTextSelectionEnabled="True"
                           Foreground="{ThemeResource SubtleTextBrush}" />
                <TextBlock Grid.Column="1"
                           Text="{Binding DisplayValue}"
                           FontSize="13"
                           TextWrapping="Wrap"
                           IsTextSelectionEnabled="True"
                           Foreground="{Binding DisplayTone, Converter={StaticResource ToneBrush}}" />
              </Grid>
            </DataTemplate>
            """);
    }

    private static Brush GroupTone(Group group)
    {
        var tone = group.Items.Select(i => i.Tone).FirstOrDefault(t => t != Tone.Normal);
        return ThemeBrush(tone switch
        {
            Tone.Good => "ToneGoodBrush",
            Tone.Warn => "ToneWarnBrush",
            Tone.Bad => "ToneBadBrush",
            Tone.Muted => "ToneMutedBrush",
            _ => "ToneNormalBrush",
        });
    }

    // ---------------------------------------------------------------- table
    private FrameworkElement BuildTable()
    {
        var card = NewCard();
        var content = new StackPanel { Spacing = 0 };
        var columns = _section!.Columns;
        var rows = FilteredRows();

        content.Children.Add(new TextBlock
        {
            Text = _section.DisplayTitle,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        content.Children.Add(TableGrid(columns, rows, header: true));

        content.Children.Add(CleanList(rows.Select(r => new RowView(r, columns)).ToList(), TableRowTemplate(columns)));

        card.Child = content;
        return card;
    }

    private static Grid TableGrid(IReadOnlyList<TableColumn> columns, IReadOnlyList<TableRow> rows, bool header)
    {
        var grid = new Grid { Padding = new Thickness(0, header ? 6 : 4, 0, 0) };
        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns[i].Width, GridUnitType.Star) });
        }

        if (header)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                var tb = new TextBlock
                {
                    Text = columns[i].DisplayHeader,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = ThemeBrush("SubtleTextBrush"),
                    HorizontalAlignment = columns[i].Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    TextAlignment = columns[i].Right ? TextAlignment.Right : TextAlignment.Left,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                Grid.SetColumn(tb, i);
                grid.Children.Add(tb);
            }
        }

        return grid;
    }

    private static DataTemplate TableRowTemplate(IReadOnlyList<TableColumn> columns)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        sb.Append("<Grid Padding=\"0,4\" ColumnSpacing=\"10\">");
        sb.Append("<Grid.ColumnDefinitions>");
        foreach (var c in columns)
        {
            sb.Append("<ColumnDefinition Width=\"").Append(c.Width.ToString(CultureInfo.InvariantCulture)).Append("*\" />");
        }

        sb.Append("</Grid.ColumnDefinitions>");
        for (var i = 0; i < columns.Count; i++)
        {
            var align = columns[i].Right ? "Right" : "Left";
            sb.Append("<TextBlock Grid.Column=\"").Append(i).Append("\" Text=\"{Binding C").Append(i).Append("}\" FontSize=\"13\" TextTrimming=\"CharacterEllipsis\" TextWrapping=\"NoWrap\" IsTextSelectionEnabled=\"True\" HorizontalAlignment=\"").Append(align).Append('"');
            if (i == 0)
            {
                sb.Append(" Foreground=\"{Binding Tone, Converter={StaticResource ToneBrush}}\"");
            }
            else
            {
                sb.Append(" Foreground=\"{ThemeResource ToneNormalBrush}\"");
            }

            sb.Append(" />");
        }

        sb.Append("</Grid></DataTemplate>");
        return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(sb.ToString());
    }

    /// <summary>Row + column definitions so the shared template can pick the right cell.</summary>
    public sealed class RowView
    {
        public RowView(TableRow row, IReadOnlyList<TableColumn> columns)
        {
            Row = row;
            Columns = columns;
        }

        public TableRow Row { get; }

        public IReadOnlyList<TableColumn> Columns { get; }

        public Tone Tone => Row.Tone;

        public string C0 => Row.C0;

        public string C1 => Row.C1;

        public string C2 => Row.C2;

        public string C3 => Row.C3;

        public string C4 => Row.C4;

        public string C5 => Row.C5;
    }

    // ---------------------------------------------------------------- helpers
    private static Border NewCard()
    {
        var border = new Border
        {
            Background = ThemeBrush("CardBackgroundBrush"),
            BorderBrush = ThemeBrush("CardBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 12),
        };

        return border;
    }

    /// <summary>
    /// A ListView without item chrome. Built in code, with the template applied directly, because
    /// runtime resource lookups on the page are not reliable during the first layout pass.
    /// </summary>
    private static ListView CleanList(object itemsSource, DataTemplate template)
    {
        var list = new ListView
        {
            ItemsSource = itemsSource,
            ItemTemplate = template,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(0),
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            ItemContainerStyle = CleanItemStyle(),
        };

        return list;
    }

    /// <summary>
    /// Item container styling for the card lists. Set property-by-property: WinUI 3 does not allow
    /// building a <c>ControlTemplate</c> from C#, so the built-in template is kept and only the
    /// chrome is neutralised.
    /// </summary>
    private static Style CleanItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        style.Setters.Add(new Setter(Control.IsTabStopProperty, false));
        return style;
    }

    /// <summary>Resolve a brush declared in the app theme dictionaries.</summary>
    private static Brush ThemeBrush(string key)
    {
        try
        {
            if (Application.Current?.Resources is { } res)
            {
                if (res.TryGetValue(key, out var value) && value is Brush direct)
                {
                    return direct;
                }

                foreach (var themeKey in new[] { "Default", "Light", "Dark" })
                {
                    if (res.ThemeDictionaries.TryGetValue(themeKey, out var dict)
                        && dict is ResourceDictionary theme
                        && theme.TryGetValue(key, out var themed)
                        && themed is Brush brush)
                    {
                        return brush;
                    }
                }
            }
        }
        catch
        {
            // fall through to a neutral brush
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static Grid TwoColumns(FrameworkElement? left, FrameworkElement? right)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (left is not null)
        {
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);
        }

        if (right is not null)
        {
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
        }

        return grid;
    }
}
