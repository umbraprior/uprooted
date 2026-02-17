namespace Uprooted;

/// <summary>
/// Builds Avalonia control trees for Uprooted settings pages.
/// All controls created via reflection through AvaloniaReflection.
/// Styling matches Root's native settings pages:
///   - Cards: BG=#0f1923, CornerRadius=12, BorderThickness=0.5, inner padding 24px
///   - Section headers: FontSize=12, Medium(500), #66f2f2f2 (matches Root's "APP SETTINGS")
///   - Field labels: FontSize=13, Weight=450, #a3f2f2f2
///   - Body text: FontSize=13, Normal, #a3f2f2f2
///   - Page title: FontSize=20, SemiBold(600), #fff2f2f2
///   - Font: CircularXX TT (passed from native controls)
///   - Page container: StackPanel(Margin=24,24,24,0)
/// </summary>
internal static class ContentPages
{
    // Default Root colors (used as fallback when no theme active)
    private const string DefaultCardBg = "#0f1923";
    private const string DefaultCardBorder = "#19ffffff";
    private const string DefaultTextWhite = "#fff2f2f2";
    private const string DefaultTextMuted = "#a3f2f2f2";
    private const string DefaultTextDim = "#66f2f2f2";
    private const string DefaultAccentGreen = "#2A5A40";

    // Themed colors derived from the active theme engine (set per-build)
    internal static string CardBg = DefaultCardBg;
    private static string CardBorder = DefaultCardBorder;
    internal static string TextWhite = DefaultTextWhite;
    internal static string TextMuted = DefaultTextMuted;
    internal static string TextDim = DefaultTextDim;
    internal static string AccentGreen = DefaultAccentGreen;

    /// <summary>
    /// Update statics for live preview during color picker drag.
    /// Uses palette values for consistency with resource/tree updates.
    /// </summary>
    internal static void UpdateLiveColors(string accent, string bg, Dictionary<string, string>? palette)
    {
        AccentGreen = accent;

        // Use palette colors so Uprooted elements match Root's tree-walked colors
        if (palette != null &&
            palette.TryGetValue("SolidBackgroundFillColorSecondary", out var palBg) &&
            palette.TryGetValue("TextFillColorPrimary", out var palText))
        {
            CardBg = palBg;
            CardBorder = ColorUtils.WithAlpha(palText, 0x19);
            var (tr, tg, tb) = ColorUtils.ParseHex(palText);
            TextWhite = $"#FF{tr:X2}{tg:X2}{tb:X2}";
            TextMuted = $"#A3{tr:X2}{tg:X2}{tb:X2}";
            TextDim = $"#66{tr:X2}{tg:X2}{tb:X2}";
        }
        else
        {
            var textBase = ColorUtils.DeriveTextColor(bg);
            var (tr, tg, tb) = ColorUtils.ParseHex(textBase);
            CardBg = ColorUtils.Lighten(bg, 6);
            CardBorder = ColorUtils.WithAlpha(textBase, 0x19);
            TextWhite = $"#FF{tr:X2}{tg:X2}{tb:X2}";
            TextMuted = $"#A3{tr:X2}{tg:X2}{tb:X2}";
            TextDim = $"#66{tr:X2}{tg:X2}{tb:X2}";
        }
    }

    /// <summary>
    /// Update static color fields from the active theme engine.
    /// Called at the start of each page build so all cards/text use themed colors.
    /// </summary>
    private static void ApplyThemedColors(ThemeEngine? themeEngine)
    {
        if (themeEngine?.ActiveThemeName == null || themeEngine.ActiveThemeName == "default-dark")
        {
            // Reset to defaults
            CardBg = DefaultCardBg;
            CardBorder = DefaultCardBorder;
            TextWhite = DefaultTextWhite;
            TextMuted = DefaultTextMuted;
            TextDim = DefaultTextDim;
            AccentGreen = DefaultAccentGreen;
            return;
        }

        var bg = themeEngine.GetBgPrimary();
        var accent = themeEngine.GetAccentColor();
        AccentGreen = accent;

        // Use palette-derived colors for consistency with resource/tree updates
        var palette = themeEngine.GetPalette();
        if (palette != null &&
            palette.TryGetValue("SolidBackgroundFillColorSecondary", out var palBg) &&
            palette.TryGetValue("TextFillColorPrimary", out var palText))
        {
            CardBg = palBg;
            CardBorder = ColorUtils.WithAlpha(palText, 0x19);
            var (tr, tg, tb) = ColorUtils.ParseHex(palText);
            TextWhite = $"#FF{tr:X2}{tg:X2}{tb:X2}";
            TextMuted = $"#A3{tr:X2}{tg:X2}{tb:X2}";
            TextDim = $"#66{tr:X2}{tg:X2}{tb:X2}";
        }
        else
        {
            var textBase = ColorUtils.DeriveTextColor(bg);
            var (tr, tg, tb) = ColorUtils.ParseHex(textBase);
            CardBg = ColorUtils.Lighten(bg, 6);
            CardBorder = ColorUtils.WithAlpha(textBase, 0x19);
            TextWhite = $"#FF{tr:X2}{tg:X2}{tb:X2}";
            TextMuted = $"#A3{tr:X2}{tg:X2}{tb:X2}";
            TextDim = $"#66{tr:X2}{tg:X2}{tb:X2}";
        }
    }

    // NSFW filter reference for live config updates from settings UI
    internal static NsfwFilter? NsfwFilterInstance { get; set; }

    public static object? BuildPage(string pageName, AvaloniaReflection r,
        UprootedSettings settings, object? nativeFontFamily = null,
        ThemeEngine? themeEngine = null, Action? onThemeChanged = null)
    {
        Logger.Log("ContentPages", $"BuildPage('{pageName}') called");
        EnsureStaticInit();

        object? page = null;
        try
        {
            page = pageName switch
            {
                "uprooted" => BuildUprootedPage(r, settings, nativeFontFamily, themeEngine),
                "plugins" => BuildPluginsPage(r, settings, nativeFontFamily, themeEngine),
                "themes" => BuildThemesPage(r, settings, nativeFontFamily, themeEngine, onThemeChanged),
                _ => null
            };
        }
        catch (Exception ex)
        {
            Logger.LogException("ContentPages", $"BuildPage('{pageName}') EXCEPTION", ex);
            return null;
        }

        if (page == null)
        {
            Logger.Log("ContentPages", $"BuildPage('{pageName}') returned null");
            return null;
        }

        // Set background on the returned ScrollViewer so our page covers any
        // stale colors from Root's content panel behind it
        var bg = themeEngine?.ActiveThemeName != null &&
                 themeEngine.ActiveThemeName != "default-dark"
            ? themeEngine.GetBgPrimary()
            : "#0D1521";
        r.SetBackground(page, bg);
        r.SetTag(page, "uprooted-content");

        Logger.Log("ContentPages", $"BuildPage('{pageName}') OK");
        return page;
    }

    private static void ApplyFont(AvaloniaReflection r, object? control, object? fontFamily)
    {
        if (control != null && fontFamily != null)
            r.SetFontFamily(control, fontFamily);
    }

    private static object? BuildUprootedPage(AvaloniaReflection r, UprootedSettings settings, object? font, ThemeEngine? themeEngine = null)
    {
        ApplyThemedColors(themeEngine);
        var page = r.CreateStackPanel(vertical: true, spacing: 0);
        if (page == null) return null;
        r.SetMargin(page, 24, 24, 24, 0);
        r.SetTag(page, "uprooted-content");

        // Page title
        var pageTitle = r.CreateTextBlock("Uprooted", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);

        // Card 1: Uprooted identity
        var identityCard = CreateCard(r);
        if (identityCard != null)
        {
            r.SetMargin(identityCard, 0, 20, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            // Section header + version badge
            var titleRow = r.CreateStackPanel(vertical: false, spacing: 12);
            var title = CreateSectionHeader(r, "UPROOTED", font);
            r.AddChild(titleRow, title);

            var versionText = r.CreateTextBlock($"v{settings.Version}", 11, TextWhite);
            ApplyFont(r, versionText, font);
            var versionBadge = r.CreateBorder(AccentGreen, 8, versionText);
            r.SetPadding(versionBadge, 8, 2, 8, 2);
            r.SetVerticalAlignment(versionBadge, "Center");
            r.AddChild(titleRow, versionBadge);
            r.AddChild(cardContent, titleRow);

            // About text
            var aboutText = r.CreateTextBlock(
                "A client modification framework for Root Communications. " +
                "Customize your Root experience with plugins and themes.",
                13, TextMuted);
            if (aboutText != null)
            {
                ApplyFont(r, aboutText, font);
                r.SetTextWrapping(aboutText, "Wrap");
                r.SetMargin(aboutText, 0, 16, 0, 0);
            }
            r.AddChild(cardContent, aboutText);

            r.SetBorderChild(identityCard, cardContent);
            r.AddChild(page, identityCard);
        }

        // Card 2: Status
        var statusCard = CreateCard(r);
        if (statusCard != null)
        {
            r.SetMargin(statusCard, 0, 12, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            var statusTitle = CreateSectionHeader(r, "STATUS", font);
            r.AddChild(cardContent, statusTitle);

            AddStatusField(r, cardContent, "Hook", "Loaded", AccentGreen, true, font);
            AddStatusField(r, cardContent, "Settings Injection", "Active", AccentGreen, false, font);
            var enabledCount = settings.Plugins.Count(p => p.Value);
            var totalCount = settings.Plugins.Count;
            var pluginStatus = enabledCount > 0 ? $"{enabledCount} active" : "0 loaded";
            var pluginColor = enabledCount > 0 ? AccentGreen : TextDim;
            AddStatusField(r, cardContent, "Plugins", pluginStatus, pluginColor, false, font);
            var activeTheme = themeEngine?.ActiveThemeName;
            var hasTheme = activeTheme != null && activeTheme != "default-dark";
            var themeStatus = hasTheme ? "Active (" + activeTheme + ")" : "Not active";
            var themeColor = hasTheme ? AccentGreen : TextDim;
            AddStatusField(r, cardContent, "Theme Override", themeStatus, themeColor, false, font);

            r.SetBorderChild(statusCard, cardContent);
            r.AddChild(page, statusCard);
        }

        // Card 3: Links
        var linksCard = CreateCard(r);
        if (linksCard != null)
        {
            r.SetMargin(linksCard, 0, 12, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            var linksTitle = CreateSectionHeader(r, "LINKS", font);
            r.AddChild(cardContent, linksTitle);

            AddLinkField(r, cardContent, "GitHub", "github.com/watchthelight/uprooted", true, font);
            AddLinkField(r, cardContent, "Website", "uprooted.sh", false, font);

            r.SetBorderChild(linksCard, cardContent);
            r.AddChild(page, linksCard);
        }

        // Card 4: Hook Info (technical details)
        var hookCard = CreateCard(r);
        if (hookCard != null)
        {
            r.SetMargin(hookCard, 0, 12, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            var hookTitle = CreateSectionHeader(r, "HOOK INFO", font);
            r.AddChild(cardContent, hookTitle);

            var hookText = r.CreateTextBlock(
                "Uprooted is loaded via .NET CLR Profiler into Root's process. " +
                "It persists across restarts via environment variables. " +
                "The profiler hooks into Root's .NET runtime to inject the Uprooted module.",
                13, TextMuted);
            if (hookText != null)
            {
                ApplyFont(r, hookText, font);
                r.SetTextWrapping(hookText, "Wrap");
                r.SetMargin(hookText, 0, 16, 0, 0);
            }
            r.AddChild(cardContent, hookText);

            r.SetBorderChild(hookCard, cardContent);
            r.AddChild(page, hookCard);
        }

        // Card 5: Log file path (diagnostics)
        var logCard = CreateCard(r);
        if (logCard != null)
        {
            r.SetMargin(logCard, 0, 12, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            var logTitle = CreateSectionHeader(r, "DIAGNOSTICS", font);
            r.AddChild(cardContent, logTitle);

            var logPathText = r.CreateTextBlock(
                "Log file: " + Logger.GetLogPath(),
                12, TextDim);
            if (logPathText != null)
            {
                ApplyFont(r, logPathText, font);
                r.SetTextWrapping(logPathText, "Wrap");
                r.SetMargin(logPathText, 0, 12, 0, 0);
            }
            r.AddChild(cardContent, logPathText);

            r.SetBorderChild(logCard, cardContent);
            r.AddChild(page, logCard);
        }

        // Bottom padding
        var spacer = r.CreateStackPanel(vertical: true, spacing: 0);
        if (spacer != null)
        {
            spacer.GetType().GetProperty("Height")?.SetValue(spacer, 24.0);
            r.AddChild(page, spacer);
        }

        return r.CreateScrollViewer(page);
    }

    // Plain class to avoid ValueTuple TypeLoadException in profiler injection context
    private sealed class PluginInfo
    {
        public string Id = "";
        public string DisplayName = "";
        public string Version = "";
        public string Description = "";
        public bool DefaultEnabled;
        public bool HasSettings;
        public int TestingStatus;
    }

    // Testing status levels: 0=Untested (red), 1=Alpha (orange), 2=Beta (yellow), 3=Closed (green)
    private static readonly string[] TestingLabels = { "Untested", "Alpha", "Beta", "Closed" };
    private static readonly string[] TestingColors = { "#E04040", "#E08030", "#C0A820", "#40A050" };

    // Known plugins metadata
    private static PluginInfo[]? KnownPlugins;

    private static void EnsureStaticInit()
    {
        if (KnownPlugins != null) return;
        try
        {
            KnownPlugins = new PluginInfo[]
            {
                new() { Id = "sentry-blocker", DisplayName = "Sentry Blocker", Version = "0.2.3",
                    Description = "Blocks Sentry error tracking to protect your privacy. Intercepts network requests to *.sentry.io.",
                    DefaultEnabled = true, HasSettings = false, TestingStatus = 1 },
                new() { Id = "themes", DisplayName = "Themes", Version = "0.2.3",
                    Description = "Built-in theme engine. Apply preset or custom color themes to Root's UI.",
                    DefaultEnabled = true, HasSettings = false, TestingStatus = 2 },
                new() { Id = "content-filter", DisplayName = "Content Filter", Version = "0.2.3",
                    Description = "Automatically blur images classified as NSFW using Google Cloud Vision's SafeSearch API.",
                    DefaultEnabled = false, HasSettings = true, TestingStatus = 0 },
            };
            Logger.Log("ContentPages", $"Static init OK: {KnownPlugins.Length} plugins");
        }
        catch (Exception ex)
        {
            Logger.LogException("ContentPages", "Static init FAILED", ex);
            KnownPlugins = Array.Empty<PluginInfo>();
        }
    }

    // Filter dropdown state (singleton, like ColorPickerPopup)
    private static object? _filterOverlay;
    private static object? _filterBackdrop;
    private static object? _filterPanel;

    // Plugin info lightbox state
    private static object? _infoOverlay;
    private static object? _infoBackdrop;
    private static object? _infoPanel;

    // Plugin settings lightbox state
    private static object? _settingsOverlay;
    private static object? _settingsBackdrop;
    private static object? _settingsPanel;

    private static object? BuildPluginsPage(AvaloniaReflection r, UprootedSettings settings, object? font, ThemeEngine? themeEngine = null)
    {
        ApplyThemedColors(themeEngine);
        var page = r.CreateStackPanel(vertical: true, spacing: 0);
        if (page == null) return null;
        r.SetMargin(page, 24, 24, 24, 0);
        r.SetTag(page, "uprooted-content");

        // Page title
        var pageTitle = r.CreateTextBlock("Plugins", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);

        // State for search and filter
        string[] searchText = { "" };
        int[] filterMode = { 0 };
        Action? rebuildGrid = null;

        // "FILTERS" section header
        var filtersHeader = CreateSectionHeader(r, "FILTERS", font);
        if (filtersHeader != null)
        {
            r.SetMargin(filtersHeader, 0, 20, 0, 0);
            r.AddChild(page, filtersHeader);
        }

        // Search + Filter row using Panel (overlay layout: search stretches, filter right-aligned)
        var searchFilterRow = r.CreatePanel();
        object? filterTextBlock = null;
        if (searchFilterRow != null)
        {
            r.SetMargin(searchFilterRow, 0, 10, 0, 0);

            // Search box
            var searchBox = r.CreateTextBox("Search for a plugin...", "", 100);
            if (searchBox != null)
            {
                searchBox.GetType().GetProperty("FontSize")?.SetValue(searchBox, 13.0);
                r.SetHeight(searchBox, 36);
                r.SetBackground(searchBox, ColorUtils.Lighten(CardBg, 5));
                r.SetForeground(searchBox, TextWhite);
                ApplyFont(r, searchBox, font);
                r.SetHorizontalAlignment(searchBox, "Stretch");
                r.SetMargin(searchBox, 0, 0, 120, 0);
                if (r.CornerRadiusType != null)
                {
                    var cr = Activator.CreateInstance(r.CornerRadiusType, 8.0, 8.0, 8.0, 8.0);
                    searchBox.GetType().GetProperty("CornerRadius")?.SetValue(searchBox, cr);
                }

                var searchBoxRef = searchBox;
                r.SubscribeEvent(searchBox, "TextChanged", () =>
                {
                    searchText[0] = r.GetTextBoxText(searchBoxRef)?.Trim() ?? "";
                    rebuildGrid?.Invoke();
                });

                r.AddChild(searchFilterRow, searchBox);
            }

            // Filter button
            filterTextBlock = r.CreateTextBlock("Show All \u25BE", 13, TextMuted);
            ApplyFont(r, filterTextBlock, font);
            var filterBtnBg = ColorUtils.Lighten(CardBg, 5);
            var filterBtn = r.CreateBorder(filterBtnBg, 8);
            if (filterBtn != null)
            {
                r.SetPadding(filterBtn, 14, 8, 14, 8);
                r.SetBorderChild(filterBtn, filterTextBlock);
                r.SetHorizontalAlignment(filterBtn, "Right");
                r.SetVerticalAlignment(filterBtn, "Center");
                r.SetCursorHand(filterBtn);
                SetBorderStroke(r, filterBtn, CardBorder, 0.5);

                var btnRef = filterBtn;
                var txtRef = filterTextBlock;
                r.SubscribeEvent(filterBtn, "PointerPressed", () =>
                {
                    ShowFilterDropdown(r, btnRef, txtRef, filterMode,
                        () => rebuildGrid?.Invoke(), font);
                });
                r.SubscribeEvent(filterBtn, "PointerEntered", () =>
                    r.SetBackground(btnRef, ColorUtils.Lighten(filterBtnBg, 6)));
                r.SubscribeEvent(filterBtn, "PointerExited", () =>
                    r.SetBackground(btnRef, filterBtnBg));

                r.AddChild(searchFilterRow, filterBtn);
            }

            r.AddChild(page, searchFilterRow);
        }

        // Plugin count label
        var countLabel = r.CreateTextBlock($"{KnownPlugins.Length} plugins", 13, TextDim);
        if (countLabel != null)
        {
            ApplyFont(r, countLabel, font);
            r.SetMargin(countLabel, 0, 16, 0, 0);
        }
        r.AddChild(page, countLabel);

        // Card container (vertical stack, rows contain 2-column grids)
        var cardContainer = r.CreateStackPanel(vertical: true, spacing: 12);
        if (cardContainer != null)
            r.SetMargin(cardContainer, 0, 12, 0, 0);
        r.AddChild(page, cardContainer);

        // Pre-build all plugin cards (parallel lists to avoid ValueTuple TypeLoadException in profiler context)
        var cardIds = new List<string>();
        var cardNames = new List<string>();
        var cardDescs = new List<string>();
        var cardObjects = new List<object>();
        foreach (var plugin in KnownPlugins)
        {
            // Content filter uses NsfwFilterEnabled as its canonical toggle
            bool isEnabled = plugin.Id == "content-filter"
                ? settings.NsfwFilterEnabled
                : (settings.Plugins.TryGetValue(plugin.Id, out var en) ? en : plugin.DefaultEnabled);
            var card = BuildPluginCard(r, settings, plugin.Id, plugin.DisplayName,
                plugin.Description, isEnabled, font, themeEngine,
                filterMode, () => r.RunOnUIThread(() => rebuildGrid?.Invoke()),
                plugin.HasSettings, plugin.TestingStatus);
            if (card != null)
            {
                cardIds.Add(plugin.Id);
                cardNames.Add(plugin.DisplayName);
                cardDescs.Add(plugin.Description);
                cardObjects.Add(card);
            }
        }

        // Track row grids and no-results message separately to avoid calling
        // GetChildren on a TextBlock (which has no Children property and crashes)
        var activeRowGrids = new List<object>();
        object?[] noResultsMsg = { null };

        // Rebuild grid closure: detaches cards, filters, and lays out 2-column rows
        rebuildGrid = () =>
        {
            // Detach cards from their row grids, then remove row grids from container
            foreach (var rowGrid in activeRowGrids)
            {
                var rowChildren = r.GetChildren(rowGrid);
                if (rowChildren != null)
                {
                    var rowCards = new List<object>();
                    foreach (var rc in rowChildren)
                        if (rc != null) rowCards.Add(rc);
                    foreach (var rc in rowCards)
                        r.RemoveChild(rowGrid, rc);
                }
                r.RemoveChild(cardContainer, rowGrid);
            }
            activeRowGrids.Clear();

            // Remove "no results" message if present
            if (noResultsMsg[0] != null)
            {
                r.RemoveChild(cardContainer, noResultsMsg[0]);
                noResultsMsg[0] = null;
            }

            // Filter cards by search text and filter mode
            var visible = new List<object>();
            for (int ci = 0; ci < cardObjects.Count; ci++)
            {
                if (!string.IsNullOrEmpty(searchText[0]))
                {
                    var q = searchText[0].ToLowerInvariant();
                    if (!cardNames[ci].ToLowerInvariant().Contains(q) && !cardDescs[ci].ToLowerInvariant().Contains(q))
                        continue;
                }
                if (filterMode[0] != 0)
                {
                    bool enabled = settings.Plugins.TryGetValue(cardIds[ci], out var en2) ? en2 : true;
                    if (filterMode[0] == 1 && !enabled) continue;
                    if (filterMode[0] == 2 && enabled) continue;
                }
                visible.Add(cardObjects[ci]);
            }

            // Build 2-column rows
            for (int i = 0; i < visible.Count; i += 2)
            {
                var rowGrid = r.CreateGrid();
                if (rowGrid == null) continue;
                r.AddGridColumn(rowGrid, 1.0);
                r.AddGridColumn(rowGrid, 1.0);

                r.SetGridColumn(visible[i], 0);
                r.SetMargin(visible[i], 0, 0, 6, 0);
                r.AddChild(rowGrid, visible[i]);

                if (i + 1 < visible.Count)
                {
                    r.SetGridColumn(visible[i + 1], 1);
                    r.SetMargin(visible[i + 1], 6, 0, 0, 0);
                    r.AddChild(rowGrid, visible[i + 1]);
                }

                r.AddChild(cardContainer, rowGrid);
                activeRowGrids.Add(rowGrid);
            }

            // Update count label
            if (countLabel != null)
            {
                var text = visible.Count == cardObjects.Count
                    ? $"{cardObjects.Count} plugins"
                    : $"{visible.Count} of {cardObjects.Count} plugins";
                r.TextBlockType?.GetProperty("Text")?.SetValue(countLabel, text);
            }

            // No results message
            if (visible.Count == 0)
            {
                var noResults = r.CreateTextBlock("No plugins match your filters.", 13, TextDim);
                ApplyFont(r, noResults, font);
                r.SetMargin(noResults, 0, 8, 0, 0);
                r.SetHorizontalAlignment(noResults, "Center");
                r.AddChild(cardContainer, noResults);
                noResultsMsg[0] = noResults;
            }
        };

        // Initial grid build
        rebuildGrid();

        // Bottom padding
        var spacer = r.CreateStackPanel(vertical: true, spacing: 0);
        if (spacer != null)
        {
            spacer.GetType().GetProperty("Height")?.SetValue(spacer, 24.0);
            r.AddChild(page, spacer);
        }

        return r.CreateScrollViewer(page);
    }

    /// <summary>
    /// Build a Vencord-style plugin card: name + toggle on top row, description below.
    /// Gear icon opens settings lightbox; info icon opens description lightbox.
    /// </summary>
    private static object? BuildPluginCard(AvaloniaReflection r, UprootedSettings settings,
        string pluginId, string displayName, string description,
        bool isEnabled, object? font, ThemeEngine? themeEngine,
        int[] filterMode, Action? onRebuildNeeded, bool hasSettings = false,
        int testingStatus = -1)
    {
        var card = CreateCard(r);
        if (card == null) return null;
        r.SetTag(card, $"uprooted-item-{pluginId}");

        var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
        if (cardContent == null) return card;
        r.SetMargin(cardContent, 16, 14, 16, 14);

        // Top row: name (left) + icons/toggle (right) using Panel overlay
        var topRow = r.CreatePanel();
        if (topRow != null)
        {
            // Plugin name - left aligned
            var nameText = r.CreateTextBlock(displayName, 14, TextWhite);
            r.SetFontWeightNumeric(nameText, 600);
            ApplyFont(r, nameText, font);
            r.SetHorizontalAlignment(nameText, "Left");
            r.SetVerticalAlignment(nameText, "Center");
            r.SetMargin(nameText, 0, 0, 80, 0);
            r.AddChild(topRow, nameText);

            // Right side: gear icon + info icon + toggle
            var rightIcons = r.CreateStackPanel(vertical: false, spacing: 10);
            if (rightIcons != null)
            {
                r.SetHorizontalAlignment(rightIcons, "Right");
                r.SetVerticalAlignment(rightIcons, "Center");

                // Gear icon - opens settings lightbox (only for plugins with settings)
                if (hasSettings)
                {
                    var gearBtnBg = ColorUtils.Lighten(CardBg, 12);
                    var gearBtn = r.CreateBorder(gearBtnBg, 11);
                    if (gearBtn != null)
                    {
                        r.SetWidth(gearBtn, 22);
                        r.SetHeight(gearBtn, 22);
                        r.SetCursorHand(gearBtn);

                        var gearText = r.CreateTextBlock("\u2699", 13, TextMuted);
                        ApplyFont(r, gearText, font);
                        r.SetHorizontalAlignment(gearText, "Center");
                        r.SetVerticalAlignment(gearText, "Center");
                        r.SetBorderChild(gearBtn, gearText);

                        var gearBtnRef = gearBtn;
                        var capturedId = pluginId;
                        var capturedName = displayName;
                        r.SubscribeEvent(gearBtn, "PointerPressed", () =>
                        {
                            ShowPluginSettingsLightbox(r, capturedId, capturedName, settings, font);
                        });
                        r.SubscribeEvent(gearBtn, "PointerEntered", () =>
                            r.SetBackground(gearBtnRef, ColorUtils.Lighten(gearBtnBg, 8)));
                        r.SubscribeEvent(gearBtn, "PointerExited", () =>
                            r.SetBackground(gearBtnRef, gearBtnBg));

                        r.AddChild(rightIcons, gearBtn);
                    }
                }

                // Info icon - opens lightbox with plugin details
                {
                    var infoBtnBg = ColorUtils.Lighten(CardBg, 12);
                    var infoBtn = r.CreateBorder(infoBtnBg, 11);
                    if (infoBtn != null)
                    {
                        r.SetWidth(infoBtn, 22);
                        r.SetHeight(infoBtn, 22);
                        r.SetCursorHand(infoBtn);

                        var infoText = r.CreateTextBlock("i", 12, TextMuted);
                        r.SetFontWeightNumeric(infoText, 600);
                        ApplyFont(r, infoText, font);
                        r.SetHorizontalAlignment(infoText, "Center");
                        r.SetVerticalAlignment(infoText, "Center");
                        r.SetBorderChild(infoBtn, infoText);

                        var infoBtnRef = infoBtn;
                        var capturedName = displayName;
                        var capturedDesc = description;
                        var capturedId = pluginId;
                        r.SubscribeEvent(infoBtn, "PointerPressed", () =>
                        {
                            ShowPluginInfoLightbox(r, capturedName, capturedDesc, capturedId, font);
                        });
                        r.SubscribeEvent(infoBtn, "PointerEntered", () =>
                            r.SetBackground(infoBtnRef, ColorUtils.Lighten(infoBtnBg, 8)));
                        r.SubscribeEvent(infoBtn, "PointerExited", () =>
                            r.SetBackground(infoBtnRef, infoBtnBg));

                        r.AddChild(rightIcons, infoBtn);
                    }
                }

                // Toggle switch
                var togglePill = BuildToggleSwitch(r, isEnabled, font, (enabled) =>
                {
                    settings.Plugins[pluginId] = enabled;

                    // Content filter uses NsfwFilterEnabled as canonical toggle
                    if (pluginId == "content-filter")
                    {
                        settings.NsfwFilterEnabled = enabled;
                        NsfwFilterInstance?.UpdateConfig();
                    }

                    if (pluginId == "themes" && themeEngine != null)
                    {
                        if (!enabled)
                        {
                            themeEngine.RevertTheme();
                            settings.ActiveTheme = "default-dark";
                        }
                        else if (settings.ActiveTheme != "default-dark")
                        {
                            if (settings.ActiveTheme == "custom")
                                themeEngine.ApplyCustomTheme(settings.CustomAccent, settings.CustomBackground);
                            else
                                themeEngine.ApplyTheme(settings.ActiveTheme);
                        }
                    }

                    try { settings.Save(); }
                    catch (Exception sx) { Logger.Log("Plugins", $"Save error: {sx.Message}"); }
                    Logger.Log("Plugins", $"Plugin '{pluginId}' toggled to {enabled}");

                    // Rebuild grid if filter is active so toggled plugins appear/disappear
                    if (filterMode[0] != 0)
                        onRebuildNeeded?.Invoke();
                });
                if (togglePill != null)
                    r.AddChild(rightIcons, togglePill);

                r.AddChild(topRow, rightIcons);
            }

            r.AddChild(cardContent, topRow);
        }

        // Description
        var descText = r.CreateTextBlock(description, 13, TextMuted);
        if (descText != null)
        {
            ApplyFont(r, descText, font);
            r.SetTextWrapping(descText, "Wrap");
            r.SetMargin(descText, 0, 8, 0, 0);
        }
        r.AddChild(cardContent, descText);

        // Testing status badge
        if (testingStatus >= 0 && testingStatus < TestingLabels.Length)
        {
            var statusLabel = TestingLabels[testingStatus];
            var statusColor = TestingColors[testingStatus];
            var badge = r.CreateBorder(statusColor + "20", 6);
            if (badge != null)
            {
                r.SetMargin(badge, 0, 10, 0, 0);
                r.SetHorizontalAlignment(badge, "Left");
                r.SetPadding(badge, 8, 3, 8, 3);
                SetBorderStroke(r, badge, statusColor + "60", 0.5);

                var badgeText = r.CreateTextBlock(statusLabel, 11, statusColor);
                r.SetFontWeightNumeric(badgeText, 500);
                ApplyFont(r, badgeText, font);
                r.SetBorderChild(badge, badgeText);

                r.AddChild(cardContent, badge);
            }
        }

        r.SetBorderChild(card, cardContent);

        // Card hover effect
        var cardBgCurrent = CardBg;
        r.SubscribeEvent(card, "PointerEntered", () =>
            r.SetBackground(card, ColorUtils.Lighten(cardBgCurrent, 4)));
        r.SubscribeEvent(card, "PointerExited", () =>
            r.SetBackground(card, cardBgCurrent));

        return card;
    }

    /// <summary>
    /// Show filter dropdown overlay below the filter button.
    /// </summary>
    private static void ShowFilterDropdown(AvaloniaReflection r, object filterBtn,
        object? filterTextBlock, int[] filterMode, Action rebuildGrid, object? font)
    {
        DismissFilterDropdown(r);

        var mainWindow = r.GetMainWindow();
        if (mainWindow == null) return;

        var overlay = r.GetOverlayLayer(mainWindow);
        if (overlay == null) return;

        _filterOverlay = overlay;

        // Position below filter button
        var btnBounds = r.GetBounds(filterBtn);
        var translated = r.TranslatePoint(filterBtn, 0, 0, overlay);
        if (btnBounds == null || translated == null) return;

        double btnX = translated.Value.X;
        double btnY = translated.Value.Y;
        double btnH = btnBounds.Value.H;
        double btnW = btnBounds.Value.W;

        var windowBounds = r.GetBounds(mainWindow);
        double windowW = windowBounds?.W ?? 800;
        double windowH = windowBounds?.H ?? 600;

        // Backdrop (click to dismiss)
        _filterBackdrop = r.CreateBorder("#01000000", 0);
        if (_filterBackdrop != null)
        {
            r.SetWidth(_filterBackdrop, windowW);
            r.SetHeight(_filterBackdrop, windowH);
            r.SetCanvasPosition(_filterBackdrop, 0, 0);
            r.SetTag(_filterBackdrop, "uprooted-no-recolor");
            r.SubscribeEvent(_filterBackdrop, "PointerPressed", () => DismissFilterDropdown(r));
            r.AddToOverlay(overlay, _filterBackdrop);
        }

        // Dropdown panel
        _filterPanel = r.CreateBorder(CardBg, 8);
        if (_filterPanel == null) return;
        r.SetTag(_filterPanel, "uprooted-no-recolor");
        SetBorderStroke(r, _filterPanel, CardBorder, 0.5);

        // Position: right-aligned with button, below it
        double dropW = 160;
        double dropX = btnX + btnW - dropW;
        if (dropX < 8) dropX = 8;
        double dropY = btnY + btnH + 4;

        r.SetCanvasPosition(_filterPanel, dropX, dropY);

        var options = r.CreateStackPanel(vertical: true, spacing: 0);
        if (options == null) return;
        r.SetMargin(options, 4, 4, 4, 4);

        var filterOptions = new[] { ("Show All", 0), ("Show Enabled", 1), ("Show Disabled", 2) };
        foreach (var (label, mode) in filterOptions)
        {
            var isActive = filterMode[0] == mode;
            var optBg = isActive ? ColorUtils.Lighten(CardBg, 8) : CardBg;
            var optBorder = r.CreateBorder(optBg, 6);
            if (optBorder == null) continue;
            r.SetCursorHand(optBorder);
            r.SetPadding(optBorder, 12, 8, 12, 8);
            r.SetWidth(optBorder, 152);

            var optColor = isActive ? TextWhite : TextMuted;
            var optWeight = isActive ? 600 : 400;
            var optText = r.CreateTextBlock(label, 13, optColor);
            r.SetFontWeightNumeric(optText, optWeight);
            ApplyFont(r, optText, font);
            r.SetBorderChild(optBorder, optText);

            var capturedMode = mode;
            var capturedLabel = label;
            var optBorderRef = optBorder;
            var optBgRef = optBg;

            r.SubscribeEvent(optBorder, "PointerPressed", () =>
            {
                filterMode[0] = capturedMode;
                if (filterTextBlock != null)
                {
                    var btnText = capturedLabel + " \u25BE";
                    r.TextBlockType?.GetProperty("Text")?.SetValue(filterTextBlock, btnText);
                }
                rebuildGrid();
                DismissFilterDropdown(r);
            });

            r.SubscribeEvent(optBorder, "PointerEntered", () =>
                r.SetBackground(optBorderRef, ColorUtils.Lighten(CardBg, 10)));
            r.SubscribeEvent(optBorder, "PointerExited", () =>
                r.SetBackground(optBorderRef, optBgRef));

            r.AddChild(options, optBorder);
        }

        r.SetBorderChild(_filterPanel, options);
        r.AddToOverlay(overlay, _filterPanel);
    }

    /// <summary>
    /// Dismiss the filter dropdown overlay.
    /// </summary>
    private static void DismissFilterDropdown(AvaloniaReflection r)
    {
        if (_filterOverlay == null) return;

        if (_filterBackdrop != null)
        {
            r.RemoveFromOverlay(_filterOverlay, _filterBackdrop);
            _filterBackdrop = null;
        }
        if (_filterPanel != null)
        {
            r.RemoveFromOverlay(_filterOverlay, _filterPanel);
            _filterPanel = null;
        }

        _filterOverlay = null;
    }

    /// <summary>
    /// Show a centered lightbox overlay with plugin info (name, description, extra content).
    /// </summary>
    private static void ShowPluginInfoLightbox(AvaloniaReflection r, string pluginName,
        string description, string pluginId, object? font)
    {
        DismissPluginInfoLightbox(r);

        var mainWindow = r.GetMainWindow();
        if (mainWindow == null) return;

        var overlay = r.GetOverlayLayer(mainWindow);
        if (overlay == null) return;

        _infoOverlay = overlay;

        var windowBounds = r.GetBounds(mainWindow);
        double windowW = windowBounds?.W ?? 800;
        double windowH = windowBounds?.H ?? 600;

        // Semi-transparent backdrop
        _infoBackdrop = r.CreateBorder("#80000000", 0);
        if (_infoBackdrop != null)
        {
            r.SetWidth(_infoBackdrop, windowW);
            r.SetHeight(_infoBackdrop, windowH);
            r.SetCanvasPosition(_infoBackdrop, 0, 0);
            r.SetTag(_infoBackdrop, "uprooted-no-recolor");
            r.SubscribeEvent(_infoBackdrop, "PointerPressed", () => DismissPluginInfoLightbox(r));
            r.AddToOverlay(overlay, _infoBackdrop);
        }

        // Lightbox card
        double cardW = 480;
        _infoPanel = r.CreateBorder(CardBg, 12);
        if (_infoPanel == null) return;
        r.SetTag(_infoPanel, "uprooted-no-recolor");
        SetBorderStroke(r, _infoPanel, CardBorder, 0.5);
        r.SetWidth(_infoPanel, cardW);

        // Center the card
        double cardX = (windowW - cardW) / 2;
        double cardY = windowH * 0.2; // 20% from top
        r.SetCanvasPosition(_infoPanel, cardX, cardY);

        var content = r.CreateStackPanel(vertical: true, spacing: 0);
        if (content == null) return;
        r.SetMargin(content, 24, 20, 24, 20);

        // Header row: plugin name (left) + close button (right)
        var headerRow = r.CreatePanel();
        if (headerRow != null)
        {
            var titleText = r.CreateTextBlock(pluginName, 18, TextWhite);
            r.SetFontWeightNumeric(titleText, 600);
            ApplyFont(r, titleText, font);
            r.SetHorizontalAlignment(titleText, "Left");
            r.SetVerticalAlignment(titleText, "Center");
            r.AddChild(headerRow, titleText);

            // Close button
            var closeBtnBg = ColorUtils.Lighten(CardBg, 12);
            var closeBtn = r.CreateBorder(closeBtnBg, 10);
            if (closeBtn != null)
            {
                r.SetWidth(closeBtn, 28);
                r.SetHeight(closeBtn, 28);
                r.SetCursorHand(closeBtn);
                r.SetHorizontalAlignment(closeBtn, "Right");
                r.SetVerticalAlignment(closeBtn, "Center");

                var closeText = r.CreateTextBlock("\u2715", 14, TextMuted);
                ApplyFont(r, closeText, font);
                r.SetHorizontalAlignment(closeText, "Center");
                r.SetVerticalAlignment(closeText, "Center");
                r.SetBorderChild(closeBtn, closeText);

                var closeBtnRef = closeBtn;
                r.SubscribeEvent(closeBtn, "PointerPressed", () => DismissPluginInfoLightbox(r));
                r.SubscribeEvent(closeBtn, "PointerEntered", () =>
                    r.SetBackground(closeBtnRef, ColorUtils.Lighten(closeBtnBg, 8)));
                r.SubscribeEvent(closeBtn, "PointerExited", () =>
                    r.SetBackground(closeBtnRef, closeBtnBg));

                r.AddChild(headerRow, closeBtn);
            }

            r.AddChild(content, headerRow);
        }

        // Description
        var descText = r.CreateTextBlock(description, 13, TextMuted);
        if (descText != null)
        {
            ApplyFont(r, descText, font);
            r.SetTextWrapping(descText, "Wrap");
            r.SetMargin(descText, 0, 14, 0, 0);
        }
        r.AddChild(content, descText);

        // Plugin-specific extra content
        if (pluginId == "sentry-blocker")
        {
            var privacyBox = BuildPrivacyInfoBox(r, font);
            if (privacyBox != null)
            {
                r.SetMargin(privacyBox, 0, 14, 0, 0);
                r.AddChild(content, privacyBox);
            }
        }
        else if (pluginId == "content-filter")
        {
            var howBox = BuildContentFilterInfoBox(r, font);
            if (howBox != null)
            {
                r.SetMargin(howBox, 0, 14, 0, 0);
                r.AddChild(content, howBox);
            }
        }

        r.SetBorderChild(_infoPanel, content);
        r.AddToOverlay(overlay, _infoPanel);
    }

    /// <summary>
    /// Dismiss the plugin info lightbox overlay.
    /// </summary>
    private static void DismissPluginInfoLightbox(AvaloniaReflection r)
    {
        if (_infoOverlay == null) return;

        if (_infoBackdrop != null)
        {
            r.RemoveFromOverlay(_infoOverlay, _infoBackdrop);
            _infoBackdrop = null;
        }
        if (_infoPanel != null)
        {
            r.RemoveFromOverlay(_infoOverlay, _infoPanel);
            _infoPanel = null;
        }

        _infoOverlay = null;
    }

    /// <summary>
    /// Build the privacy info box shown on the sentry-blocker card.
    /// </summary>
    private static object? BuildPrivacyInfoBox(AvaloniaReflection r, object? font)
    {
        var infoBg = ColorUtils.Lighten(CardBg, 4);
        var box = r.CreateBorder(infoBg, 8);
        if (box == null) return null;
        SetBorderStroke(r, box, "#15ffffff", 0.5);

        var content = r.CreateStackPanel(vertical: true, spacing: 6);
        if (content == null) return box;
        r.SetMargin(content, 16, 14, 16, 14);

        var headerText = r.CreateTextBlock(
            "Without this plugin, Root sends the following to Sentry's servers (not Root's servers):",
            12, TextMuted);
        if (headerText != null)
        {
            r.SetFontWeightNumeric(headerText, 450);
            ApplyFont(r, headerText, font);
            r.SetTextWrapping(headerText, "Wrap");
        }
        r.AddChild(content, headerText);

        var items = new[]
        {
            "\u2022  Your IP address (on every error event)",
            "\u2022  Session replays: DOM snapshots, mouse movements, input values",
            "\u2022  Authentication headers including your Bearer token",
            "\u2022  Application traces and logs",
        };
        foreach (var item in items)
        {
            var itemText = r.CreateTextBlock(item, 12, TextDim);
            if (itemText != null)
            {
                ApplyFont(r, itemText, font);
                r.SetTextWrapping(itemText, "Wrap");
                r.SetMargin(itemText, 4, 0, 0, 0);
            }
            r.AddChild(content, itemText);
        }

        r.SetBorderChild(box, content);
        return box;
    }

    /// <summary>
    /// Build a pill-shaped toggle switch (44x24) with animated thumb.
    /// ON: AccentGreen pill, thumb right. OFF: dim pill, thumb left.
    /// </summary>
    private static object? BuildToggleSwitch(AvaloniaReflection r, bool initialState, object? font,
        Action<bool>? onToggled = null)
    {
        bool state = initialState;

        var dimColor = ColorUtils.Lighten(CardBg, 18);
        var pillColor = state ? AccentGreen : dimColor;

        // Outer pill
        var pill = r.CreateBorder(pillColor, 12);
        if (pill == null) return null;
        r.SetWidth(pill, 44);
        r.SetHeight(pill, 24);
        r.SetCursorHand(pill);
        r.SetTag(pill, "uprooted-toggle-pill");

        // Thumb (circle inside)
        var thumb = r.CreateBorder("#FFFFFFFF", 9);
        if (thumb != null)
        {
            r.SetWidth(thumb, 18);
            r.SetHeight(thumb, 18);
            r.SetHorizontalAlignment(thumb, state ? "Right" : "Left");
            r.SetVerticalAlignment(thumb, "Center");
            r.SetMargin(thumb, 3, 0, 3, 0);
        }
        r.SetBorderChild(pill, thumb);

        // Capture the current accent for closures
        var accentColor = AccentGreen;

        // Click handler
        r.SubscribeEvent(pill, "PointerPressed", () =>
        {
            state = !state;
            // Update visuals
            r.SetBackground(pill, state ? accentColor : dimColor);
            if (thumb != null)
                r.SetHorizontalAlignment(thumb, state ? "Right" : "Left");

            onToggled?.Invoke(state);
        });

        // Hover effects
        r.SubscribeEvent(pill, "PointerEntered", () =>
        {
            var hoverColor = state
                ? ColorUtils.Lighten(accentColor, 10)
                : ColorUtils.Lighten(dimColor, 8);
            r.SetBackground(pill, hoverColor);
        });
        r.SubscribeEvent(pill, "PointerExited", () =>
        {
            r.SetBackground(pill, state ? accentColor : dimColor);
        });

        return pill;
    }

    private static object? BuildThemesPage(AvaloniaReflection r, UprootedSettings settings,
        object? font, ThemeEngine? themeEngine = null, Action? onThemeChanged = null)
    {
        ApplyThemedColors(themeEngine);
        var page = r.CreateStackPanel(vertical: true, spacing: 0);
        if (page == null) return null;
        r.SetMargin(page, 24, 24, 24, 0);
        r.SetTag(page, "uprooted-content");

        // Page title
        var pageTitle = r.CreateTextBlock("Themes", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);

        // === PRESET THEMES section ===
        var presetHeader = CreateSectionHeader(r, "PRESET THEMES", font);
        if (presetHeader != null)
        {
            r.SetMargin(presetHeader, 0, 20, 0, 12);
            r.AddChild(page, presetHeader);
        }

        // All three preset cards in a horizontal row
        var allPresets = new[]
        {
            ("Default",  "default-dark", "#0D1521", "#3B6AF8", "Root's default"),
            ("Crimson",  "crimson",      "#1A0A0A", "#C42B1C", "Deep red accent"),
            ("Loki",     "loki",         "#0F1210", "#2A5A40", "Gold and green"),
        };

        var presetsRow = r.CreateStackPanel(vertical: false, spacing: 8);
        if (presetsRow != null)
        {
            for (int i = 0; i < allPresets.Length; i++)
            {
                var (displayName, themeId, bgColor, accentColor, description) = allPresets[i];
                bool isActive = settings.ActiveTheme == themeId;
                var card = BuildThemeCard(r, displayName, themeId, bgColor, accentColor,
                    description, isActive, font, themeEngine, settings, onThemeChanged);
                if (card != null)
                {
                    r.AddChild(presetsRow, card);
                }
            }

            r.AddChild(page, presetsRow);
        }

        // === CUSTOM THEME section ===
        var customHeader = CreateSectionHeader(r, "CUSTOM THEME", font);
        if (customHeader != null)
        {
            r.SetMargin(customHeader, 0, 16, 0, 12);
            r.AddChild(page, customHeader);
        }

        var customSection = BuildCustomThemeSection(r, settings, font, themeEngine, onThemeChanged);
        if (customSection != null)
            r.AddChild(page, customSection);

        // About themes card
        var aboutCard = CreateCard(r);
        if (aboutCard != null)
        {
            r.SetMargin(aboutCard, 0, 16, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);

            var aboutTitle = CreateSectionHeader(r, "ABOUT THEMES", font);
            r.AddChild(cardContent, aboutTitle);

            var aboutText = r.CreateTextBlock(
                "Themes modify Avalonia's FluentTheme resource dictionary at runtime. " +
                "They change accent colors, backgrounds, control fills, and text colors " +
                "across the entire native UI. Custom themes derive all shades from your " +
                "chosen accent and background colors. Your theme persists across restarts.",
                13, TextMuted);
            if (aboutText != null)
            {
                ApplyFont(r, aboutText, font);
                r.SetTextWrapping(aboutText, "Wrap");
                r.SetMargin(aboutText, 0, 16, 0, 0);
            }
            r.AddChild(cardContent, aboutText);

            r.SetBorderChild(aboutCard, cardContent);
            r.AddChild(page, aboutCard);
        }

        var spacer = r.CreateStackPanel(vertical: true, spacing: 0);
        if (spacer != null)
        {
            spacer.GetType().GetProperty("Height")?.SetValue(spacer, 24.0);
            r.AddChild(page, spacer);
        }

        return r.CreateScrollViewer(page);
    }

    /// <summary>
    /// Build the custom theme card with hex inputs for accent + background, color swatches, and apply button.
    /// </summary>
    private static object? BuildCustomThemeSection(AvaloniaReflection r, UprootedSettings settings,
        object? font, ThemeEngine? themeEngine, Action? onThemeChanged)
    {
        bool isActive = settings.ActiveTheme == "custom";
        var inactiveBorder = ColorUtils.Lighten(CardBg, 12);
        var borderColor = isActive ? settings.CustomAccent : inactiveBorder;

        var card = r.CreateBorder(CardBg, 12);
        if (card == null) return null;
        SetBorderStroke(r, card, borderColor, isActive ? 1.5 : 1.0);

        var outerContent = r.CreateStackPanel(vertical: true, spacing: 0);
        if (outerContent == null) return card;
        r.SetMargin(outerContent, 20, 16, 20, 16);

        // Header row with radio indicator -- clickable to activate custom theme
        var headerRow = r.CreateStackPanel(vertical: false, spacing: 12);
        if (headerRow != null)
        {
            r.SetVerticalAlignment(headerRow, "Center");
            r.SetBackground(headerRow, "Transparent"); // Required for hit-testing

            var radioOuter = r.CreateBorder(null, 4);
            if (radioOuter != null)
            {
                r.SetWidth(radioOuter, 20);
                r.SetHeight(radioOuter, 20);
                SetBorderStroke(r, radioOuter, isActive ? settings.CustomAccent : ColorUtils.Lighten(CardBg, 25), 2.0);
                r.SetVerticalAlignment(radioOuter, "Center");
                if (isActive)
                {
                    var innerDot = r.CreateBorder(settings.CustomAccent, 2);
                    if (innerDot != null)
                    {
                        r.SetWidth(innerDot, 10);
                        r.SetHeight(innerDot, 10);
                        r.SetMargin(innerDot, 3, 3, 3, 3);
                    }
                    r.SetBorderChild(radioOuter, innerDot);
                }
                r.AddChild(headerRow, radioOuter);
            }

            var textStack = r.CreateStackPanel(vertical: true, spacing: 2);
            if (textStack != null)
            {
                var nameText = r.CreateTextBlock("Custom", 14, TextWhite);
                r.SetFontWeightNumeric(nameText, 450);
                ApplyFont(r, nameText, font);
                r.AddChild(textStack, nameText);

                var descText = r.CreateTextBlock("Pick your own accent and background", 12, TextMuted);
                ApplyFont(r, descText, font);
                r.AddChild(textStack, descText);

                r.AddChild(headerRow, textStack);
            }

            // Click handler to activate custom theme
            r.SetCursorHand(headerRow);
            r.SubscribeEvent(headerRow, "PointerPressed", () =>
            {
                try
                {
                    if (settings.ActiveTheme == "custom") return; // already active

                    Logger.Log("Theme", "Custom theme card clicked");
                    settings.ActiveTheme = "custom";

                    // Apply custom theme with current saved colors
                    themeEngine?.ApplyCustomTheme(settings.CustomAccent, settings.CustomBackground);

                    try { settings.Save(); }
                    catch (Exception sx) { Logger.Log("Theme", "Save error: " + sx.Message); }

                    if (onThemeChanged != null)
                    {
                        r.RunOnUIThread(() =>
                        {
                            try { onThemeChanged.Invoke(); }
                            catch (Exception rx) { Logger.Log("Theme", "Rebuild error: " + rx.Message); }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Theme", "Custom theme activate error: " + ex.Message);
                }
            });

            r.AddChild(outerContent, headerRow);
        }

        // Mutable holders so each color picker callback can read the other textbox
        // (which may not exist yet at closure creation time)
        object?[] accentTextBoxRef = new object?[1];
        object?[] bgTextBoxRef = new object?[1];

        // Track last-known values to skip no-op TextChanged events during page init.
        // Avalonia fires TextChanged when controls attach to the visual tree, even
        // if the text hasn't actually changed. We skip those to avoid unnecessary
        // UpdateCustomThemeLive calls and saves during page construction.
        string[] lastAccent = new[] { settings.CustomAccent };
        string[] lastBg = new[] { settings.CustomBackground };

        // Debounced auto-save: saves settings 1s after last color change
        System.Threading.Timer? saveTimer = null;
        Action debounceSave = () =>
        {
            saveTimer?.Dispose();
            saveTimer = new System.Threading.Timer(_ =>
            {
                try { settings.Save(); }
                catch (Exception sx) { Logger.Log("Theme", "Auto-save error: " + sx.Message); }
            }, null, 1000, System.Threading.Timeout.Infinite);
        };

        // Accent row: label + textbox + swatch
        var accentSwatch = r.CreateBorder(settings.CustomAccent, 4);
        var accentRow = BuildColorInputRow(r, "Accent", settings.CustomAccent, font, accentSwatch,
            accentHex =>
            {
                // Skip if this is the same value (e.g. from page init TextChanged)
                if (string.Equals(accentHex, lastAccent[0], StringComparison.OrdinalIgnoreCase)) return;
                lastAccent[0] = accentHex;
                settings.CustomAccent = accentHex;
                settings.ActiveTheme = "custom";
                var bgVal = r.GetTextBoxText(bgTextBoxRef[0])?.Trim() ?? settings.CustomBackground;
                if (ColorUtils.IsValidHex(bgVal))
                    themeEngine?.UpdateCustomThemeLive(accentHex, bgVal);
                debounceSave();
            });
        if (accentRow != null)
        {
            r.SetMargin(accentRow, 32, 16, 0, 0);
            r.AddChild(outerContent, accentRow);
        }

        // Background row: label + textbox + swatch
        var bgSwatch = r.CreateBorder(settings.CustomBackground, 4);
        var bgRow = BuildColorInputRow(r, "Background", settings.CustomBackground, font, bgSwatch,
            bgHex =>
            {
                // Skip if this is the same value (e.g. from page init TextChanged)
                if (string.Equals(bgHex, lastBg[0], StringComparison.OrdinalIgnoreCase)) return;
                lastBg[0] = bgHex;
                settings.CustomBackground = bgHex;
                settings.ActiveTheme = "custom";
                var accentVal = r.GetTextBoxText(accentTextBoxRef[0])?.Trim() ?? settings.CustomAccent;
                if (ColorUtils.IsValidHex(accentVal))
                    themeEngine?.UpdateCustomThemeLive(accentVal, bgHex);
                debounceSave();
            });
        if (bgRow != null)
        {
            r.SetMargin(bgRow, 32, 10, 0, 0);
            r.AddChild(outerContent, bgRow);
        }

        // Fill in the mutable holders now that both rows exist
        accentTextBoxRef[0] = GetTextBoxFromRow(r, accentRow);
        bgTextBoxRef[0] = GetTextBoxFromRow(r, bgRow);

        // Apply button
        var applyRow = r.CreateStackPanel(vertical: false, spacing: 0);
        if (applyRow != null)
        {
            r.SetMargin(applyRow, 32, 16, 0, 0);

            var applyBtn = r.CreateBorder(AccentGreen, 8);
            if (applyBtn != null)
            {
                r.SetCursorHand(applyBtn);
                var applyText = r.CreateTextBlock("Apply Custom", 13, TextWhite);
                r.SetFontWeightNumeric(applyText, 500);
                ApplyFont(r, applyText, font);
                r.SetPadding(applyBtn, 16, 6, 16, 6);
                r.SetBorderChild(applyBtn, applyText);

                // Get textbox references for reading values
                var accentTextBox = GetTextBoxFromRow(r, accentRow);
                var bgTextBox = GetTextBoxFromRow(r, bgRow);

                r.SubscribeEvent(applyBtn, "PointerPressed", () =>
                {
                    try
                    {
                        var accentVal = r.GetTextBoxText(accentTextBox)?.Trim() ?? "";
                        var bgVal = r.GetTextBoxText(bgTextBox)?.Trim() ?? "";

                        if (!ColorUtils.IsValidHex(accentVal) || !ColorUtils.IsValidHex(bgVal))
                        {
                            Logger.Log("Theme", "Invalid custom hex values: accent=" + accentVal + " bg=" + bgVal);
                            return;
                        }

                        settings.CustomAccent = accentVal;
                        settings.CustomBackground = bgVal;
                        settings.ActiveTheme = "custom";

                        themeEngine?.ApplyCustomTheme(accentVal, bgVal);

                        try { settings.Save(); }
                        catch (Exception sx) { Logger.Log("Theme", "Save error: " + sx.Message); }

                        if (onThemeChanged != null)
                        {
                            r.RunOnUIThread(() =>
                            {
                                try { onThemeChanged.Invoke(); }
                                catch (Exception rx) { Logger.Log("Theme", "Rebuild error: " + rx.Message); }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Theme", "Custom theme apply error: " + ex.Message);
                    }
                });

                // Hover effects
                var btnAccent = AccentGreen;
                r.SubscribeEvent(applyBtn, "PointerEntered", () =>
                    r.SetBackground(applyBtn, ColorUtils.Lighten(btnAccent, 15)));
                r.SubscribeEvent(applyBtn, "PointerExited", () =>
                    r.SetBackground(applyBtn, btnAccent));

                r.AddChild(applyRow, applyBtn);
            }

            r.AddChild(outerContent, applyRow);
        }

        r.SetBorderChild(card, outerContent);
        return card;
    }

    /// <summary>
    /// Build a row: [Label] [TextBox] [ColorSwatch]
    /// </summary>
    private static object? BuildColorInputRow(AvaloniaReflection r, string label,
        string initialValue, object? font, object? swatch,
        Action<string>? onColorChanged = null)
    {
        var row = r.CreateStackPanel(vertical: false, spacing: 10);
        if (row == null) return null;
        r.SetVerticalAlignment(row, "Center");

        // Label
        var labelText = r.CreateTextBlock(label, 13, TextMuted);
        r.SetFontWeightNumeric(labelText, 450);
        ApplyFont(r, labelText, font);
        r.SetWidth(labelText, 90);
        r.AddChild(row, labelText);

        // TextBox
        var textBox = r.CreateTextBox("#RRGGBB", initialValue, 7);
        if (textBox != null)
        {
            r.SetWidth(textBox, 100);
            textBox.GetType().GetProperty("FontSize")?.SetValue(textBox, 13.0);
            r.SetBackground(textBox, ColorUtils.Lighten(CardBg, 5));
            r.SetForeground(textBox, TextWhite);
            ApplyFont(r, textBox, font);
            r.SetTag(textBox, "uprooted-color-input");

            // Update swatch preview + trigger live theme preview on text change
            r.SubscribeEvent(textBox, "TextChanged", () =>
            {
                var text = r.GetTextBoxText(textBox)?.Trim() ?? "";
                if (ColorUtils.IsValidHex(text))
                {
                    if (swatch != null)
                        r.SetBackground(swatch, text);
                    onColorChanged?.Invoke(text);
                }
            });

            r.AddChild(row, textBox);
        }

        // Color swatch (clickable -- opens color picker popup)
        if (swatch != null)
        {
            r.SetWidth(swatch, 24);
            r.SetHeight(swatch, 24);
            SetBorderStroke(r, swatch, "#40ffffff", 1.0);
            r.AddChild(row, swatch);

            r.SetCursorHand(swatch);
            r.SubscribeEvent(swatch, "PointerPressed", () =>
            {
                ColorPickerPopup.Show(r, swatch, textBox, onColorChanged);
            });
        }

        return row;
    }

    /// <summary>
    /// Find the TextBox child inside a color input row (tagged "uprooted-color-input").
    /// </summary>
    private static object? GetTextBoxFromRow(AvaloniaReflection r, object? row)
    {
        if (row == null) return null;
        var children = r.GetChildren(row);
        if (children == null) return null;
        foreach (var child in children)
        {
            if (child != null && r.GetTag(child) == "uprooted-color-input")
                return child;
        }
        return null;
    }

    /// <summary>
    /// Build a compact theme card for 3-column grid layout.
    /// Structure: Border (rounded) containing vertical layout with
    /// preview swatch on top, radio + name below.
    /// </summary>
    private static object? BuildThemeCard(AvaloniaReflection r, string displayName,
        string themeId, string bgColor, string accentColor, string description,
        bool isActive, object? font, ThemeEngine? themeEngine,
        UprootedSettings settings, Action? onThemeChanged)
    {
        var borderColor = isActive ? accentColor : ColorUtils.Lighten(CardBg, 12);
        var card = r.CreateBorder(CardBg, 12);
        if (card == null) return null;
        SetBorderStroke(r, card, borderColor, isActive ? 1.5 : 1.0);
        r.SetCursorHand(card);
        r.SetWidth(card, 200);

        // Vertical layout: preview on top, radio + name below
        var outerLayout = r.CreateStackPanel(vertical: true, spacing: 0);
        if (outerLayout == null) return null;
        r.SetMargin(outerLayout, 14, 14, 14, 14);

        // Theme preview swatch at top
        var preview = BuildThemePreview(r, bgColor, accentColor);
        if (preview != null)
        {
            r.SetHorizontalAlignment(preview, "Center");
            r.AddChild(outerLayout, preview);
        }

        // Bottom row: radio indicator + name
        var bottomRow = r.CreateStackPanel(vertical: false, spacing: 10);
        if (bottomRow != null)
        {
            r.SetMargin(bottomRow, 0, 12, 0, 0);
            r.SetVerticalAlignment(bottomRow, "Center");

            // Radio indicator
            var radioOuter = r.CreateBorder(null, 4);
            if (radioOuter != null)
            {
                r.SetWidth(radioOuter, 18);
                r.SetHeight(radioOuter, 18);
                SetBorderStroke(r, radioOuter, isActive ? accentColor : ColorUtils.Lighten(CardBg, 25), 2.0);
                r.SetVerticalAlignment(radioOuter, "Center");
                if (isActive)
                {
                    var innerDot = r.CreateBorder(accentColor, 2);
                    if (innerDot != null)
                    {
                        r.SetWidth(innerDot, 8);
                        r.SetHeight(innerDot, 8);
                        r.SetMargin(innerDot, 3, 3, 3, 3);
                    }
                    r.SetBorderChild(radioOuter, innerDot);
                }
                r.AddChild(bottomRow, radioOuter);
            }

            // Name + description
            var textStack = r.CreateStackPanel(vertical: true, spacing: 1);
            if (textStack != null)
            {
                r.SetVerticalAlignment(textStack, "Center");
                var nameText = r.CreateTextBlock(displayName, 13, TextWhite);
                r.SetFontWeightNumeric(nameText, 500);
                ApplyFont(r, nameText, font);
                r.AddChild(textStack, nameText);

                var descText = r.CreateTextBlock(description, 11, TextMuted);
                ApplyFont(r, descText, font);
                r.AddChild(textStack, descText);

                r.AddChild(bottomRow, textStack);
            }

            r.AddChild(outerLayout, bottomRow);
        }

        r.SetBorderChild(card, outerLayout);

        // Click handler for theme switching
        r.SubscribeEvent(card, "PointerPressed", () =>
        {
            try
            {
                Logger.Log("Theme", "Theme card clicked: " + themeId);
                if (themeEngine == null) return;

                if (themeId == "default-dark")
                {
                    themeEngine.RevertTheme();
                    settings.ActiveTheme = "default-dark";
                }
                else
                {
                    themeEngine.ApplyTheme(themeId);
                    settings.ActiveTheme = themeId;
                }

                // Save in background to avoid MissingMethodException in event context
                try { settings.Save(); }
                catch (Exception sx) { Logger.Log("Theme", "Save error: " + sx.Message); }

                // Defer page rebuild via dispatcher to avoid modifying visual tree during event
                if (onThemeChanged != null)
                {
                    r.RunOnUIThread(() =>
                    {
                        try { onThemeChanged.Invoke(); }
                        catch (Exception rx) { Logger.Log("Theme", "Rebuild error: " + rx.Message); }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Theme", "Theme switch error: " + ex.Message);
            }
        });

        // Hover effect -- capture current CardBg for the closure
        var cardBgCurrent = CardBg;
        r.SubscribeEvent(card, "PointerEntered", () =>
        {
            if (!isActive)
                r.SetBackground(card, ColorUtils.Lighten(cardBgCurrent, 5));
        });
        r.SubscribeEvent(card, "PointerExited", () =>
        {
            if (!isActive)
                r.SetBackground(card, cardBgCurrent);
        });

        return card;
    }

    /// <summary>
    /// Build a mini preview swatch showing a simplified mockup of the theme colors.
    /// </summary>
    private static object? BuildThemePreview(AvaloniaReflection r, string bgColor, string accentColor)
    {
        // Outer frame -- tagged to prevent tree walker from recoloring
        var frame = r.CreateBorder("#00000000", 6);
        if (frame == null) return null;
        r.SetTag(frame, "uprooted-no-recolor");
        SetBorderStroke(r, frame, "#30ffffff", 0.5);
        r.SetWidth(frame, 100);
        r.SetHeight(frame, 56);

        var inner = r.CreateStackPanel(vertical: true, spacing: 0);
        if (inner == null) return frame;

        // Accent bar at top
        var accentBar = r.CreateBorder(accentColor, 0);
        if (accentBar != null)
        {
            r.SetHeight(accentBar, 6);
            // Top corners only
            if (r.CornerRadiusType != null)
            {
                var cr = Activator.CreateInstance(r.CornerRadiusType, 5.0, 5.0, 0.0, 0.0);
                accentBar.GetType().GetProperty("CornerRadius")?.SetValue(accentBar, cr);
            }
            r.AddChild(inner, accentBar);
        }

        // Body with bg color
        var body = r.CreateBorder(bgColor, 0);
        if (body != null)
        {
            r.SetHeight(body, 50);
            if (r.CornerRadiusType != null)
            {
                var cr = Activator.CreateInstance(r.CornerRadiusType, 0.0, 0.0, 5.0, 5.0);
                body.GetType().GetProperty("CornerRadius")?.SetValue(body, cr);
            }

            // Mini layout inside: sidebar + content area
            var bodyLayout = r.CreateStackPanel(vertical: false, spacing: 2);
            if (bodyLayout != null)
            {
                r.SetMargin(bodyLayout, 4, 4, 4, 4);

                var sidebar = r.CreateBorder("#15ffffff", 2);
                if (sidebar != null)
                {
                    r.SetWidth(sidebar, 22);
                    r.SetHeight(sidebar, 38);
                    r.AddChild(bodyLayout, sidebar);
                }

                var content = r.CreateBorder("#0Bffffff", 2);
                if (content != null)
                {
                    r.SetWidth(content, 64);
                    r.SetHeight(content, 38);
                    r.AddChild(bodyLayout, content);
                }

                r.SetBorderChild(body, bodyLayout);
            }

            r.AddChild(inner, body);
        }

        r.SetBorderChild(frame, inner);
        return frame;
    }

    // ===== Plugin Settings Lightbox =====

    /// <summary>
    /// Show a centered lightbox overlay with plugin-specific settings.
    /// </summary>
    private static void ShowPluginSettingsLightbox(AvaloniaReflection r, string pluginId,
        string pluginName, UprootedSettings settings, object? font)
    {
        DismissPluginSettingsLightbox(r);

        var mainWindow = r.GetMainWindow();
        if (mainWindow == null) return;

        var overlay = r.GetOverlayLayer(mainWindow);
        if (overlay == null) return;

        _settingsOverlay = overlay;

        var windowBounds = r.GetBounds(mainWindow);
        double windowW = windowBounds?.W ?? 800;
        double windowH = windowBounds?.H ?? 600;

        // Semi-transparent backdrop
        _settingsBackdrop = r.CreateBorder("#80000000", 0);
        if (_settingsBackdrop != null)
        {
            r.SetWidth(_settingsBackdrop, windowW);
            r.SetHeight(_settingsBackdrop, windowH);
            r.SetCanvasPosition(_settingsBackdrop, 0, 0);
            r.SetTag(_settingsBackdrop, "uprooted-no-recolor");
            r.SubscribeEvent(_settingsBackdrop, "PointerPressed", () => DismissPluginSettingsLightbox(r));
            r.AddToOverlay(overlay, _settingsBackdrop);
        }

        // Lightbox card
        double cardW = 480;
        _settingsPanel = r.CreateBorder(CardBg, 12);
        if (_settingsPanel == null) return;
        r.SetTag(_settingsPanel, "uprooted-no-recolor");
        SetBorderStroke(r, _settingsPanel, CardBorder, 0.5);
        r.SetWidth(_settingsPanel, cardW);

        // Center the card
        double cardX = (windowW - cardW) / 2;
        double cardY = windowH * 0.12;
        r.SetCanvasPosition(_settingsPanel, cardX, cardY);

        var content = r.CreateStackPanel(vertical: true, spacing: 0);
        if (content == null) return;
        r.SetMargin(content, 24, 20, 24, 20);

        // Header row: plugin name + close button
        var headerRow = r.CreatePanel();
        if (headerRow != null)
        {
            var titleText = r.CreateTextBlock(pluginName + " Settings", 18, TextWhite);
            r.SetFontWeightNumeric(titleText, 600);
            ApplyFont(r, titleText, font);
            r.SetHorizontalAlignment(titleText, "Left");
            r.SetVerticalAlignment(titleText, "Center");
            r.AddChild(headerRow, titleText);

            // Close button
            var closeBtnBg = ColorUtils.Lighten(CardBg, 12);
            var closeBtn = r.CreateBorder(closeBtnBg, 10);
            if (closeBtn != null)
            {
                r.SetWidth(closeBtn, 28);
                r.SetHeight(closeBtn, 28);
                r.SetCursorHand(closeBtn);
                r.SetHorizontalAlignment(closeBtn, "Right");
                r.SetVerticalAlignment(closeBtn, "Center");

                var closeText = r.CreateTextBlock("\u2715", 14, TextMuted);
                ApplyFont(r, closeText, font);
                r.SetHorizontalAlignment(closeText, "Center");
                r.SetVerticalAlignment(closeText, "Center");
                r.SetBorderChild(closeBtn, closeText);

                var closeBtnRef = closeBtn;
                r.SubscribeEvent(closeBtn, "PointerPressed", () => DismissPluginSettingsLightbox(r));
                r.SubscribeEvent(closeBtn, "PointerEntered", () =>
                    r.SetBackground(closeBtnRef, ColorUtils.Lighten(closeBtnBg, 8)));
                r.SubscribeEvent(closeBtn, "PointerExited", () =>
                    r.SetBackground(closeBtnRef, closeBtnBg));

                r.AddChild(headerRow, closeBtn);
            }

            r.AddChild(content, headerRow);
        }

        // Plugin-specific settings content
        if (pluginId == "content-filter")
            BuildContentFilterSettings(r, content, settings, font);

        r.SetBorderChild(_settingsPanel, content);
        r.AddToOverlay(overlay, _settingsPanel);
    }

    private static void DismissPluginSettingsLightbox(AvaloniaReflection r)
    {
        if (_settingsOverlay == null) return;

        if (_settingsBackdrop != null)
        {
            r.RemoveFromOverlay(_settingsOverlay, _settingsBackdrop);
            _settingsBackdrop = null;
        }
        if (_settingsPanel != null)
        {
            r.RemoveFromOverlay(_settingsOverlay, _settingsPanel);
            _settingsPanel = null;
        }

        _settingsOverlay = null;
    }

    /// <summary>
    /// Build the content filter settings UI inside a lightbox container.
    /// Includes API key input + save button and sensitivity radio buttons.
    /// </summary>
    private static void BuildContentFilterSettings(AvaloniaReflection r, object content,
        UprootedSettings settings, object? font)
    {
        // API Key section
        var apiTitle = CreateSectionHeader(r, "GOOGLE CLOUD VISION API KEY", font);
        if (apiTitle != null)
        {
            r.SetMargin(apiTitle, 0, 18, 0, 0);
            r.AddChild(content, apiTitle);
        }

        var apiTextBox = r.CreateTextBox("Enter your API key", settings.NsfwApiKey, 100);
        if (apiTextBox != null)
        {
            apiTextBox.GetType().GetProperty("FontSize")?.SetValue(apiTextBox, 13.0);
            r.SetHeight(apiTextBox, 36);
            r.SetBackground(apiTextBox, ColorUtils.Lighten(CardBg, 5));
            r.SetForeground(apiTextBox, TextWhite);
            ApplyFont(r, apiTextBox, font);
            r.SetMargin(apiTextBox, 0, 12, 0, 0);
            if (r.CornerRadiusType != null)
            {
                var cr = Activator.CreateInstance(r.CornerRadiusType, 8.0, 8.0, 8.0, 8.0);
                apiTextBox.GetType().GetProperty("CornerRadius")?.SetValue(apiTextBox, cr);
            }
            r.AddChild(content, apiTextBox);
        }

        var helperText = r.CreateTextBlock(
            "Get a key from console.cloud.google.com -- SafeSearch costs ~$1.50/1,000 images",
            12, TextDim);
        if (helperText != null)
        {
            ApplyFont(r, helperText, font);
            r.SetTextWrapping(helperText, "Wrap");
            r.SetMargin(helperText, 0, 8, 0, 0);
        }
        r.AddChild(content, helperText);

        // Save button
        var saveBtnRow = r.CreateStackPanel(vertical: false, spacing: 0);
        if (saveBtnRow != null)
        {
            r.SetMargin(saveBtnRow, 0, 12, 0, 0);

            var saveBtn = r.CreateBorder(AccentGreen, 8);
            if (saveBtn != null)
            {
                r.SetCursorHand(saveBtn);
                var saveBtnText = r.CreateTextBlock("Save API Key", 13, TextWhite);
                r.SetFontWeightNumeric(saveBtnText, 500);
                ApplyFont(r, saveBtnText, font);
                r.SetPadding(saveBtn, 16, 6, 16, 6);
                r.SetBorderChild(saveBtn, saveBtnText);

                var apiTextBoxRef = apiTextBox;
                r.SubscribeEvent(saveBtn, "PointerPressed", () =>
                {
                    var key = r.GetTextBoxText(apiTextBoxRef)?.Trim() ?? "";
                    settings.NsfwApiKey = key;
                    try { settings.Save(); }
                    catch (Exception sx) { Logger.Log("ContentFilter", $"Save error: {sx.Message}"); }
                    NsfwFilterInstance?.UpdateConfig();
                    Logger.Log("ContentFilter", $"API key saved (length={key.Length})");
                });

                var btnAccent = AccentGreen;
                r.SubscribeEvent(saveBtn, "PointerEntered", () =>
                    r.SetBackground(saveBtn, ColorUtils.Lighten(btnAccent, 15)));
                r.SubscribeEvent(saveBtn, "PointerExited", () =>
                    r.SetBackground(saveBtn, btnAccent));

                r.AddChild(saveBtnRow, saveBtn);
            }

            r.AddChild(content, saveBtnRow);
        }

        // Sensitivity section
        var sensTitle = CreateSectionHeader(r, "SENSITIVITY", font);
        if (sensTitle != null)
        {
            r.SetMargin(sensTitle, 0, 20, 0, 0);
            r.AddChild(content, sensTitle);
        }

        var options = new[]
        {
            ("Strict", 0.4, "Blurs on POSSIBLE and above"),
            ("Normal", 0.6, "Blurs on LIKELY and above"),
            ("Relaxed", 0.8, "Blurs only VERY_LIKELY"),
        };

        var radioIndicators = new List<(object outer, object? inner, double threshold)>();

        foreach (var (label, threshold, desc) in options)
        {
            bool isActive = Math.Abs(settings.NsfwThreshold - threshold) < 0.01;
            var optionRow = r.CreateStackPanel(vertical: false, spacing: 12);
            if (optionRow == null) continue;
            r.SetMargin(optionRow, 0, 12, 0, 0);
            r.SetCursorHand(optionRow);
            r.SetBackground(optionRow, "Transparent");

            var radioOuter = r.CreateBorder(null, 4);
            object? radioInner = null;
            if (radioOuter != null)
            {
                r.SetWidth(radioOuter, 18);
                r.SetHeight(radioOuter, 18);
                SetBorderStroke(r, radioOuter,
                    isActive ? AccentGreen : ColorUtils.Lighten(CardBg, 25), 2.0);
                r.SetVerticalAlignment(radioOuter, "Center");

                if (isActive)
                {
                    radioInner = r.CreateBorder(AccentGreen, 2);
                    if (radioInner != null)
                    {
                        r.SetWidth(radioInner, 8);
                        r.SetHeight(radioInner, 8);
                        r.SetMargin(radioInner, 3, 3, 3, 3);
                    }
                    r.SetBorderChild(radioOuter, radioInner);
                }

                r.AddChild(optionRow, radioOuter);
                radioIndicators.Add((radioOuter, radioInner, threshold));
            }

            var textStack = r.CreateStackPanel(vertical: true, spacing: 2);
            if (textStack != null)
            {
                r.SetVerticalAlignment(textStack, "Center");
                var nameText = r.CreateTextBlock(label, 13, TextWhite);
                r.SetFontWeightNumeric(nameText, 450);
                ApplyFont(r, nameText, font);
                r.AddChild(textStack, nameText);

                var descText = r.CreateTextBlock(desc, 12, TextMuted);
                ApplyFont(r, descText, font);
                r.AddChild(textStack, descText);

                r.AddChild(optionRow, textStack);
            }

            var capturedThreshold = threshold;
            r.SubscribeEvent(optionRow, "PointerPressed", () =>
            {
                settings.NsfwThreshold = capturedThreshold;
                try { settings.Save(); }
                catch (Exception sx) { Logger.Log("ContentFilter", $"Save error: {sx.Message}"); }
                NsfwFilterInstance?.UpdateConfig();
                Logger.Log("ContentFilter", $"Sensitivity set to {capturedThreshold}");

                foreach (var (outer, inner, th) in radioIndicators)
                {
                    bool selected = Math.Abs(th - capturedThreshold) < 0.01;
                    SetBorderStroke(r, outer,
                        selected ? AccentGreen : ColorUtils.Lighten(CardBg, 25), 2.0);
                    if (selected)
                    {
                        var dot = r.CreateBorder(AccentGreen, 2);
                        if (dot != null)
                        {
                            r.SetWidth(dot, 8);
                            r.SetHeight(dot, 8);
                            r.SetMargin(dot, 3, 3, 3, 3);
                        }
                        r.SetBorderChild(outer, dot);
                    }
                    else
                    {
                        r.SetBorderChild(outer, null);
                    }
                }
            });

            r.AddChild(content, optionRow);
        }
    }

    /// <summary>
    /// Build the "How It Works" info box for the content filter info lightbox.
    /// </summary>
    private static object? BuildContentFilterInfoBox(AvaloniaReflection r, object? font)
    {
        var infoBg = ColorUtils.Lighten(CardBg, 4);
        var box = r.CreateBorder(infoBg, 8);
        if (box == null) return null;
        SetBorderStroke(r, box, "#15ffffff", 0.5);

        var boxContent = r.CreateStackPanel(vertical: true, spacing: 6);
        if (boxContent == null) return box;
        r.SetMargin(boxContent, 16, 14, 16, 14);

        var headerText = r.CreateTextBlock("HOW IT WORKS", 12, TextDim);
        if (headerText != null)
        {
            r.SetFontWeightNumeric(headerText, 500);
            ApplyFont(r, headerText, font);
        }
        r.AddChild(boxContent, headerText);

        var howText = r.CreateTextBlock(
            "Images in chat are checked against Google Cloud Vision's SafeSearch API. " +
            "While classifying, images receive a light blur. If flagged as NSFW, a full " +
            "blur is applied with a \"Click to reveal\" overlay. Results are cached by URL " +
            "to avoid redundant API calls. The filter fails open -- if the API is unreachable " +
            "or returns an error, images are shown normally. No images are stored or sent anywhere " +
            "except to Google's Vision API for classification.",
            12, TextMuted);
        if (howText != null)
        {
            ApplyFont(r, howText, font);
            r.SetTextWrapping(howText, "Wrap");
            r.SetMargin(howText, 0, 6, 0, 0);
        }
        r.AddChild(boxContent, howText);

        r.SetBorderChild(box, boxContent);
        return box;
    }

    // ===== Card and field builders matching Root's native style =====

    /// <summary>
    /// Create a section header matching Root's "APP SETTINGS" style:
    /// FontSize=12, FontWeight=Medium(500), Fg=#66f2f2f2
    /// </summary>
    private static object? CreateSectionHeader(AvaloniaReflection r, string text, object? font)
    {
        var header = r.CreateTextBlock(text, 12, TextDim);
        r.SetFontWeightNumeric(header, 500);
        ApplyFont(r, header, font);
        return header;
    }

    /// <summary>
    /// Create a card: BG=#0f1923, CornerRadius=12, BorderThickness=0.5
    /// </summary>
    private static object? CreateCard(AvaloniaReflection r)
    {
        var card = r.CreateBorder(CardBg, 12);
        if (card == null) return null;
        SetBorderStroke(r, card, CardBorder, 0.5);
        return card;
    }

    /// <summary>
    /// Status field matching Root's label-value pattern.
    /// </summary>
    private static void AddStatusField(AvaloniaReflection r, object? panel,
        string label, string value, string valueColor, bool first, object? font)
    {
        var row = r.CreateStackPanel(vertical: false, spacing: 0);
        if (row == null) return;
        r.SetMargin(row, 0, first ? 16 : 12, 0, 0);

        var labelText = r.CreateTextBlock(label, 13, TextMuted);
        r.SetFontWeightNumeric(labelText, 450);
        ApplyFont(r, labelText, font);
        r.AddChild(row, labelText);

        var separator = r.CreateTextBlock(" \u2022 ", 13, TextDim);
        ApplyFont(r, separator, font);
        r.AddChild(row, separator);

        var valueText = r.CreateTextBlock(value, 13, valueColor);
        r.SetFontWeightNumeric(valueText, 450);
        ApplyFont(r, valueText, font);
        r.AddChild(row, valueText);

        r.AddChild(panel, row);
    }

    /// <summary>
    /// Link field matching Root's label pattern.
    /// </summary>
    private static void AddLinkField(AvaloniaReflection r, object? panel,
        string label, string url, bool first, object? font)
    {
        var row = r.CreateStackPanel(vertical: false, spacing: 0);
        if (row == null) return;
        r.SetMargin(row, 0, first ? 16 : 12, 0, 0);

        var labelText = r.CreateTextBlock(label, 13, TextMuted);
        r.SetFontWeightNumeric(labelText, 450);
        ApplyFont(r, labelText, font);
        r.SetMargin(labelText, 0, 0, 12, 0);
        r.AddChild(row, labelText);

        var urlText = r.CreateTextBlock(url, 13, AccentGreen);
        r.SetFontWeightNumeric(urlText, 450);
        ApplyFont(r, urlText, font);
        r.AddChild(row, urlText);

        r.AddChild(panel, row);
    }


    private static void SetBorderStroke(AvaloniaReflection r, object? border, string hex, double width)
    {
        if (border == null) return;
        var brush = r.CreateBrush(hex);
        border.GetType().GetProperty("BorderBrush")?.SetValue(border, brush);

        if (r.ThicknessType != null)
        {
            var thickness = Activator.CreateInstance(r.ThicknessType, width, width, width, width);
            border.GetType().GetProperty("BorderThickness")?.SetValue(border, thickness);
        }
    }
}
