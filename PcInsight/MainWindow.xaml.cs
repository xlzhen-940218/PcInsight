using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PcInsight.Core;
using PcInsight.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace PcInsight;

public sealed partial class MainWindow : Window
{
    private Snapshot? _snapshot;
    private Section? _current;
    private DispatcherQueueTimer? _liveTimer;
    private string _filter = string.Empty;
    private bool _scanning;
    private bool _navReady;
    private int _completed;

    /// <summary>True once every collector has reported in.</summary>
    private bool AllLoaded => _completed >= PcInfo.Jobs.Length;

    public MainWindow()
    {
        UiLog.Reset();
        UiLog.Write("MainWindow ctor begin");
        InitializeComponent();
        UiLog.Write("InitializeComponent done");

        Title = "PC Insight";

        // Extend the XAML content into the title bar. The custom title element can only be
        // attached once it has loaded into the visual tree: calling SetTitleBar here, before
        // the first layout pass, is a no-op and leaves the native (black) title bar on screen.
        ExtendsContentIntoTitleBar = true;
        AppTitleBar.Loaded += OnTitleBarLoaded;
        RootGrid.ActualThemeChanged += (_, _) => ApplyTitleBarColors();

        FitWindowToScreen();
        UiLog.Write("window chrome configured");

        LangZh.IsChecked = Babel.L.IsChinese;
        LangEn.IsChecked = !Babel.L.IsChinese;

        Babel.L.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;

        RootGrid.Loaded += async (_, _) =>
        {
            UiLog.Write("RootGrid loaded");
            await RescanAsync();
        };

        RootGrid.SizeChanged += (_, _) => ApplyAdaptiveChrome();

        UiLog.Write("MainWindow ctor done");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Babel.L.LanguageChanged -= OnLanguageChanged;
        _liveTimer?.Stop();
    }

    /// <summary>
    /// Choose a comfortable window size without ever exceeding the work area. The display API
    /// reports physical pixels while XAML works in effective pixels, so the work area is divided
    /// by the window's DPI scale first.
    /// </summary>
    private void FitWindowToScreen()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
            if (scale <= 0.1)
            {
                scale = 1.0;
            }

            var workWidth = (int)(work.Width / scale);
            var workHeight = (int)(work.Height / scale);

            var width = Math.Clamp(Math.Min(1280, (int)(workWidth * 0.94)), 520, Math.Max(520, workWidth));
            var height = Math.Clamp(Math.Min(880, (int)(workHeight * 0.94)), 420, Math.Max(420, workHeight));

            AppWindow.Resize(new SizeInt32(width, height));
            UiLog.Write($"window set to {width}x{height} DIP (work area {workWidth}x{workHeight} DIP, scale {scale:N2})");
        }
        catch (Exception ex)
        {
            UiLog.Write("window sizing failed: " + ex.Message);
        }

        ApplyAdaptiveChrome();
    }

    /// <summary>
    /// Attaches the XAML title bar once its element is part of the loaded visual tree, then
    /// paints the system-drawn caption buttons to match the XAML theme. Setting only
    /// ButtonBackgroundColor before the window exists left the buttons in the light-theme
    /// default (a white patch on the dark title bar), so the full palette is applied.
    /// </summary>
    private void OnTitleBarLoaded(object sender, RoutedEventArgs e)
    {
        SetTitleBar(AppTitleBar);

        try
        {
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        }
        catch
        {
            // Tall caption buttons are unavailable on older Windows builds.
        }

        ApplyTitleBarColors();
        UiLog.Write("custom title bar attached");
    }

    private void ApplyTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;

        var dark = RootGrid.ActualTheme == ElementTheme.Dark;
        tb.ButtonForegroundColor = ThemeColor("TextFillColorPrimaryBrush", dark ? Colors.White : Colors.Black);
        tb.ButtonInactiveForegroundColor = ThemeColor("TextFillColorDisabledBrush", Windows.UI.Color.FromArgb(255, 150, 150, 150));
        tb.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x14, 255, 255, 255) : Windows.UI.Color.FromArgb(0x0A, 0, 0, 0);
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x29, 255, 255, 255) : Windows.UI.Color.FromArgb(0x14, 0, 0, 0);
        tb.ButtonPressedForegroundColor = tb.ButtonForegroundColor;
    }

    /// <summary>Read a solid XAML theme resource colour, with a fallback for safety.</summary>
    private static Windows.UI.Color ThemeColor(string resourceKey, Windows.UI.Color fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(resourceKey, out var value) == true
                && value is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }
        catch
        {
            // fall through
        }

        return fallback;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void ApplyAdaptiveChrome()
    {
        try
        {
            var width = RootGrid.ActualWidth > 0 ? RootGrid.ActualWidth : AppWindow.ClientSize.Width;
            var mode = width >= 900 ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
            if (Nav.PaneDisplayMode != mode)
            {
                Nav.PaneDisplayMode = mode;
                UiLog.Write($"adaptive chrome: width={(int)width} pane={mode}");
            }

            var open = width >= 1180;
            if (Nav.IsPaneOpen != open)
            {
                Nav.IsPaneOpen = open;
            }

            // The caption buttons own the far-right corner of the custom title bar; on narrow
            // windows the search box is the first control to yield.
            var searchVisible = width >= 900 ? Visibility.Visible : Visibility.Collapsed;
            if (SearchBox.Visibility != searchVisible)
            {
                SearchBox.Visibility = searchVisible;
            }
        }
        catch (Exception ex)
        {
            UiLog.Write("adaptive chrome failed: " + ex.Message);
        }
    }

    // ==================================================================== scanning
    private async Task RescanAsync()
    {
        if (_scanning)
        {
            return;
        }

        _scanning = true;
        RescanButton.IsEnabled = false;
        ScanPanel.Visibility = Visibility.Visible;
        ScanBar.Value = 0;
        ScanBar.IsIndeterminate = true;
        UiLog.WriteSection("RescanAsync");

        try
        {
            // 1) Seed every page immediately so the shell is usable straight away; each collector
            //    then fills its own page as soon as it finishes.
            var snapshot = SeedSections();
            _snapshot = snapshot;
            _current = null;
            _completed = 0;

            BuildNavigation(snapshot);
            ShowSection(snapshot.Sections.FirstOrDefault());
            StatusText.Text = Babel.L["status.scanning"];
            StatusRight.Text = "0 / " + PcInfo.Jobs.Length;
            TitleSubtitle.Text = string.Empty;

            // 2) Collect in parallel, marshalling each finished page back to the UI thread.
            var progress = new Progress<Section>(OnSectionReady);
            await Task.Run(() => PcInfo.ScanAsync(progress)).ConfigureAwait(true);

            UiLog.Write("scan complete in " + snapshot.Duration.TotalSeconds.ToString("N1") + "s");
            StatusText.Text = Str.Format("status.scanDone", Babel.L.Current, snapshot.Duration.TotalSeconds.ToString("N1"));
            StatusRight.Text = Str.Format("status.items", Babel.L.Current, snapshot.Sections.Sum(s => s.ItemCount).ToString("N0"));
            TitleSubtitle.Text = snapshot.Sections.FirstOrDefault(s => s.Key == "overview")?.Summary ?? string.Empty;

            StartLiveTimer();
        }
        catch (Exception ex)
        {
            UiLog.Write("RESCAN FAILED: " + ex);
            StatusText.Text = ex.Message;
            try
            {
                SectionView.ShowMessage(ex.ToString());
            }
            catch (Exception inner)
            {
                UiLog.Write("showing error failed: " + inner.Message);
            }
        }
        finally
        {
            _scanning = false;
            RescanButton.IsEnabled = true;
            ScanPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Snapshot containing one loading placeholder per page, in a stable order.</summary>
    private static Snapshot SeedSections()
    {
        var snapshot = new Snapshot();

        foreach (var job in PcInfo.Jobs)
        {
            var section = PcInfo.CreatePending(job.Key, job.Title, job.Glyph, job.PendingLabels);
            snapshot.Sections.Add(section);

            if (PcInfo.HardwareKeys.Contains(section.Key))
            {
                snapshot.Hardware.Add(section);
            }
            else
            {
                snapshot.Software.Add(section);
            }
        }

        return snapshot;
    }

    /// <summary>Copies a finished page into its seeded placeholder and refreshes what is on screen.</summary>
    private void OnSectionReady(Section finished)
    {
        var target = _snapshot?.Sections.FirstOrDefault(s => s.Key == finished.Key);
        if (target is null)
        {
            return;
        }

        target.MergeFrom(finished);
        _completed++;

        // The navigation items were built once while the section was still pending (badge 0);
        // refresh the badge now that the collector's counts are known.
        UpdateBadge(finished.Key, target.ItemCount);

        StatusRight.Text = _completed + " / " + PcInfo.Jobs.Length;

        if (SectionView.Section is { } shown && ReferenceEquals(shown, target))
        {
            SectionView.RefreshData();

            if (ReferenceEquals(_current, target) && Fmt.Has(target.Summary))
            {
                TitleSubtitle.Text = target.Summary;
            }
        }
    }

    private void BuildNavigation(Snapshot snapshot)
    {
        Nav.MenuItems.Clear();

        Nav.MenuItems.Add(Header("Hardware", "硬件信息"));
        foreach (var s in snapshot.Hardware)
        {
            Nav.MenuItems.Add(Item(s));
        }

        Nav.MenuItems.Add(Header("Software", "软件信息"));
        foreach (var s in snapshot.Software)
        {
            Nav.MenuItems.Add(Item(s));
        }

        _navReady = false;
    }

    private static NavigationViewItemHeader Header(string en, string zh) => new()
    {
        Content = Babel.L.IsChinese ? zh : en,
    };

    private static NavigationViewItem Item(Section s)
    {
        var item = new NavigationViewItem
        {
            Content = s.DisplayTitle,
            Tag = s.Key,
            Icon = new FontIcon { Glyph = s.Glyph, FontSize = 16 },
        };

        // A pending page shows a subtle dot; a finished one shows how much it collected.
        ApplyBadge(item, s);
        return item;
    }

    private static void ApplyBadge(NavigationViewItem item, Section s)
    {
        item.InfoBadge = s.IsLoading
            ? new InfoBadge { Value = 0, Opacity = 0.25 }
            : new InfoBadge { Value = s.ItemCount, Opacity = 0.6 };
    }

    /// <summary>Swap a navigation item's pending "dot" badge for the finished item count.</summary>
    private void UpdateBadge(string key, int count)
    {
        foreach (var obj in Nav.MenuItems)
        {
            if (obj is NavigationViewItem navItem && (navItem.Tag as string) == key)
            {
                navItem.InfoBadge = new InfoBadge { Value = count, Opacity = 0.6 };
                return;
            }
        }
    }

    private void ShowSection(Section? section)
    {
        if (section is null)
        {
            return;
        }

        UiLog.Write("ShowSection " + section.Key + " begin");
        _current = section;
        SectionView.Section = section;
        SectionView.Filter = _filter;
        SectionView.ShowOverviewCards = section.Key == "overview";
        UpdateLiveValues();
        UiLog.Write("ShowSection " + section.Key + " rendered");
        LogLayout("after show");

        _navReady = true;
        foreach (var obj in Nav.MenuItems)
        {
            if (obj is NavigationViewItem item && (item.Tag as string) == section.Key)
            {
                Nav.SelectedItem = item;
                break;
            }
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)    {
        if (!_navReady || args.SelectedItem is not NavigationViewItem item || item.Tag is not string key)
        {
            return;
        }

        var section = _snapshot?.Find(key);
        if (section is null || ReferenceEquals(section, _current))
        {
            return;
        }

        ShowSection(section);
    }

    // ==================================================================== live values
    /// <summary>
    /// Headless UI verification: activate the window, wait for the first scan, then walk the
    /// visual tree and report what was actually rendered.
    /// </summary>
    public async Task<string> RunUiCheckAsync(int settleSeconds = 20)
    {
        var report = new System.Text.StringBuilder();

        try
        {
            Activate();
        }
        catch (Exception ex)
        {
            report.AppendLine("activate failed: " + ex.Message);
        }

        // Wait for the shell to seed the pages, then for every collector to report in.
        var waited = 0;
        while ((_snapshot is null || !AllLoaded) && waited < settleSeconds * 10)
        {
            await Task.Delay(100);
            waited++;
        }

        report.AppendLine("snapshot      : " + (_snapshot is null ? "NOT READY" : _snapshot.Sections.Count + " sections"));
        report.AppendLine("pages loaded  : " + _completed + " / " + PcInfo.Jobs.Length);
        report.AppendLine("current section: " + (_current?.Key ?? "(none)"));

        // Encoding check: the exact code points of a few localized labels prove whether the
        // string table survived compilation intact (they must equal '\u7248\u672c' = 版本).
        var versionLabel = Babel.L["f.osVersion"];
        report.AppendLine("label check   : f.osVersion = \"" + versionLabel + "\" codePoints=["
            + string.Join(",", versionLabel.Select(c => "U+" + ((int)c).ToString("X4"))) + "]");
        report.AppendLine("label check   : app.title   = \"" + Babel.L["app.title"] + "\" codePoints=["
            + string.Join(",", Babel.L["app.title"].Select(c => "U+" + ((int)c).ToString("X4"))) + "]");
        report.AppendLine();

        foreach (var key in new[] { "overview", "cpu", "processes", "software" })
        {
            var section = _snapshot?.Find(key);
            if (section is null)
            {
                continue;
            }

            ShowSection(section);
            await Task.Delay(400);
            report.AppendLine(Walk(section));
        }

        var text = report.ToString();
        UiLog.WriteSection("UI CHECK");
        UiLog.Write(text);
        return text;
    }

    private string Walk(Section section)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var labels = new List<string>();
        var root = SectionView.Content as DependencyObject;
        WalkNode(root, counts, labels);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- {section.Key} ({section.DisplayTitle}) ---");
        sb.AppendLine("  visual elements : " + string.Join(", ", counts.OrderByDescending(k => k.Value).Select(k => k.Key + "=" + k.Value)));
        sb.AppendLine("  sample text     : " + string.Join(" | ", labels.Take(12)));
        return sb.ToString();
    }

    private static void WalkNode(DependencyObject? node, Dictionary<string, int> counts, List<string> labels)
    {
        if (node is null)
        {
            return;
        }

        var name = node.GetType().Name;
        counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;

        if (node is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) && labels.Count < 24)
        {
            labels.Add(Fmt.Truncate(tb.Text.Trim(), 34));
        }

        var children = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < children; i++)
        {
            WalkNode(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i), counts, labels);
        }
    }

    private void LogLayout(string phase)
    {
        try
        {
            var size = AppWindow.Size;
            var client = AppWindow.ClientSize;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var scale = GetDpiForWindow(hwnd) / 96.0;
            UiLog.Write(string.Format(
                "layout[{0}] window={1}x{2} client={3}x{4} scale={5:N2} root={6} nav={7} scroller={8} section={9} cards={10}",
                phase,
                size.Width,
                size.Height,
                client.Width,
                client.Height,
                scale,
                (int)RootGrid.ActualWidth,
                (int)Nav.ActualWidth,
                (int)ContentScroller.ActualWidth,
                (int)SectionView.ActualWidth,
                (int)SectionView.CardsWidth));
        }
        catch (Exception ex)
        {
            UiLog.Write("layout log failed: " + ex.Message);
        }
    }

    private void StartLiveTimer()
    {
        _liveTimer ??= DispatcherQueue.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(3);
        _liveTimer.Tick -= OnLiveTick;
        _liveTimer.Tick += OnLiveTick;
        _liveTimer.Start();
    }

    private void OnLiveTick(DispatcherQueueTimer sender, object args) => UpdateLiveValues();

    private async void UpdateLiveValues()
    {
        if (_snapshot is null || _current?.Key != "overview")
        {
            return;
        }

        try
        {
            var (cpu, used, total, uptime) = await Task.Run(PcInfo.LiveValues);
            SectionView.UpdateLive(cpu, used, total, uptime);
        }
        catch
        {
            // live values are cosmetic
        }
    }

    // ==================================================================== commands
    private async void OnRescanClick(object sender, RoutedEventArgs e) => await RescanAsync();

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        // Show the export/copy menu on a left click as well as a right click.
        if (sender is Button b && b.Flyout is not null)
        {
            b.Flyout.ShowAt(b);
        }

        await Task.CompletedTask;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _filter = sender.Text ?? string.Empty;
        SectionView.Filter = _filter;
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        // The flyout handles the actual switch; opening the menu is enough here.
        if (sender is Button b && b.Flyout is not null)
        {
            b.Flyout.ShowAt(b);
        }
    }

    private void OnLanguageZh(object sender, RoutedEventArgs e) => SwitchLanguage(Lang.Zh);

    private void OnLanguageEn(object sender, RoutedEventArgs e) => SwitchLanguage(Lang.En);

    private void SwitchLanguage(Lang lang)
    {
        if (Babel.L.Current == lang)
        {
            return;
        }

        Babel.L.Current = lang;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        // Rebuild everything that carries text so the switch is complete.
        _snapshot?.RaiseAll();
        SectionView.RefreshLanguage();

        if (_snapshot is not null)
        {
            var currentKey = _current?.Key;
            BuildNavigation(_snapshot);
            var section = currentKey is null ? null : _snapshot.Find(currentKey);
            if (section is not null)
            {
                _current = null;
                ShowSection(section);
            }
        }

        LangZh.IsChecked = Babel.L.IsChinese;
        LangEn.IsChecked = !Babel.L.IsChinese;
    }

    private async void OnExportJsonClick(object sender, RoutedEventArgs e) => await ExportAsync(json: true);

    private async void OnExportTextClick(object sender, RoutedEventArgs e) => await ExportAsync(json: false);

    private async Task ExportAsync(bool json)
    {
        if (_snapshot is null)
        {
            return;
        }

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PC Insight");
            Directory.CreateDirectory(dir);

            var name = "PC-Info-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + (json ? ".json" : ".txt");
            var path = Path.Combine(dir, name);
            var content = json ? Export.ToJson(_snapshot) : _snapshot.ToPlainText();
            await File.WriteAllTextAsync(path, content, new UTF8Encoding(true));

            StatusText.Text = Str.Format("status.exportDone", Babel.L.Current, path);
        }
        catch (Exception ex)
        {
            StatusText.Text = Str.Format("status.error", Babel.L.Current, ex.Message);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null)
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(_snapshot.ToPlainText());
            Clipboard.SetContent(package);
            StatusText.Text = Babel.L["status.copied"];
        }
        catch (Exception ex)
        {
            StatusText.Text = Str.Format("status.error", Babel.L.Current, ex.Message);
        }
    }
}
