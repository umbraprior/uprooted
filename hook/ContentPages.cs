namespace Uprooted;

internal static class ContentPages
{

    private const string DefaultCardBg = "#0f1923";
    private const string DefaultCardBorder = "#19ffffff";
    private const string DefaultTextWhite = "#fff2f2f2";
    private const string DefaultTextMuted = "#a3f2f2f2";
    private const string DefaultTextDim = "#66f2f2f2";
    private const string DefaultAccentGreen = "#2A5A40";


    internal static string CardBg = DefaultCardBg;
    private static string CardBorder = DefaultCardBorder;
    internal static string TextWhite = DefaultTextWhite;
    internal static string TextMuted = DefaultTextMuted;
    internal static string TextDim = DefaultTextDim;
    internal static string AccentGreen = DefaultAccentGreen;

    internal static void UpdateLiveColors(string accent, string bg, Dictionary<string, string>? palette)
    {
        AccentGreen = accent;


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

    private static void ApplyThemedColors(ThemeEngine? themeEngine)
    {
        if (themeEngine?.ActiveThemeName == null || themeEngine.ActiveThemeName == "default-dark")
        {

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

    public static object? BuildPage(string pageName, AvaloniaReflection r,
        UprootedSettings settings, object? nativeFontFamily = null,
        ThemeEngine? themeEngine = null, Action? onThemeChanged = null)
    {
        var page = pageName switch
        {
            "uprooted" => BuildUprootedPage(r, settings, nativeFontFamily, themeEngine),
            "plugins" => BuildPluginsPage(r, settings, nativeFontFamily, themeEngine),
            "themes" => BuildThemesPage(r, settings, nativeFontFamily, themeEngine, onThemeChanged),
            _ => null
        };



        if (page != null)
        {
            var bg = themeEngine?.ActiveThemeName != null &&
                     themeEngine.ActiveThemeName != "default-dark"
                ? themeEngine.GetBgPrimary()
                : "#0D1521";
            r.SetBackground(page, bg);
            r.SetTag(page, "uprooted-content");
        }

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


        var pageTitle = r.CreateTextBlock("Uprooted", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);


        var identityCard = CreateCard(r);
        if (identityCard != null)
        {
            r.SetMargin(identityCard, 0, 20, 0, 0);
            var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
            r.SetMargin(cardContent, 24, 24, 24, 24);


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


        var spacer = r.CreateStackPanel(vertical: true, spacing: 0);
        if (spacer != null)
        {
            spacer.GetType().GetProperty("Height")?.SetValue(spacer, 24.0);
            r.AddChild(page, spacer);
        }

        return r.CreateScrollViewer(page);
    }


    private static readonly (string Id, string DisplayName, string Version, string Description, bool DefaultEnabled)[] KnownPlugins =
    {
        ("sentry-blocker", "Sentry Blocker", "0.1.95",
            "Blocks Sentry error tracking to protect your privacy. Intercepts network requests to *.sentry.io.",
            true),
        ("themes", "Themes", "0.1.95",
            "Built-in theme engine. Apply preset or custom color themes to Root's UI.",
            true),
    };


    private static object? _filterOverlay;
    private static object? _filterBackdrop;
    private static object? _filterPanel;


    private static object? _infoOverlay;
    private static object? _infoBackdrop;
    private static object? _infoPanel;

    private static object? BuildPluginsPage(AvaloniaReflection r, UprootedSettings settings, object? font, ThemeEngine? themeEngine = null)
    {
        ApplyThemedColors(themeEngine);
        var page = r.CreateStackPanel(vertical: true, spacing: 0);
        if (page == null) return null;
        r.SetMargin(page, 24, 24, 24, 0);
        r.SetTag(page, "uprooted-content");


        var pageTitle = r.CreateTextBlock("Plugins", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);


        string[] searchText = { "" };
        int[] filterMode = { 0 };
        Action? rebuildGrid = null;


        var filtersHeader = CreateSectionHeader(r, "FILTERS", font);
        if (filtersHeader != null)
        {
            r.SetMargin(filtersHeader, 0, 20, 0, 0);
            r.AddChild(page, filtersHeader);
        }


        var searchFilterRow = r.CreatePanel();
        object? filterTextBlock = null;
        if (searchFilterRow != null)
        {
            r.SetMargin(searchFilterRow, 0, 10, 0, 0);


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


        var countLabel = r.CreateTextBlock($"{KnownPlugins.Length} plugins", 13, TextDim);
        if (countLabel != null)
        {
            ApplyFont(r, countLabel, font);
            r.SetMargin(countLabel, 0, 16, 0, 0);
        }
        r.AddChild(page, countLabel);


        var cardContainer = r.CreateStackPanel(vertical: true, spacing: 12);
        if (cardContainer != null)
            r.SetMargin(cardContainer, 0, 12, 0, 0);
        r.AddChild(page, cardContainer);


        var cards = new List<(string Id, string Name, string Desc, object Card)>();
        foreach (var plugin in KnownPlugins)
        {
            bool isEnabled = settings.Plugins.TryGetValue(plugin.Id, out var en) ? en : plugin.DefaultEnabled;
            var card = BuildPluginCard(r, settings, plugin.Id, plugin.DisplayName,
                plugin.Description, isEnabled, font, themeEngine,
                filterMode, () => r.RunOnUIThread(() => rebuildGrid?.Invoke()));
            if (card != null)
                cards.Add((plugin.Id, plugin.DisplayName, plugin.Description, card));
        }



        var activeRowGrids = new List<object>();
        object?[] noResultsMsg = { null };


        rebuildGrid = () =>
        {

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


            if (noResultsMsg[0] != null)
            {
                r.RemoveChild(cardContainer, noResultsMsg[0]);
                noResultsMsg[0] = null;
            }


            var visible = new List<object>();
            foreach (var (id, name, desc, card) in cards)
            {
                if (!string.IsNullOrEmpty(searchText[0]))
                {
                    var q = searchText[0].ToLowerInvariant();
                    if (!name.ToLowerInvariant().Contains(q) && !desc.ToLowerInvariant().Contains(q))
                        continue;
                }
                if (filterMode[0] != 0)
                {
                    bool enabled = settings.Plugins.TryGetValue(id, out var en2) ? en2 : true;
                    if (filterMode[0] == 1 && !enabled) continue;
                    if (filterMode[0] == 2 && enabled) continue;
                }
                visible.Add(card);
            }


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


            if (countLabel != null)
            {
                var text = visible.Count == cards.Count
                    ? $"{cards.Count} plugins"
                    : $"{visible.Count} of {cards.Count} plugins";
                r.TextBlockType?.GetProperty("Text")?.SetValue(countLabel, text);
            }


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


        rebuildGrid();


        var spacer = r.CreateStackPanel(vertical: true, spacing: 0);
        if (spacer != null)
        {
            spacer.GetType().GetProperty("Height")?.SetValue(spacer, 24.0);
            r.AddChild(page, spacer);
        }

        return r.CreateScrollViewer(page);
    }

    private static object? BuildPluginCard(AvaloniaReflection r, UprootedSettings settings,
        string pluginId, string displayName, string description,
        bool isEnabled, object? font, ThemeEngine? themeEngine,
        int[] filterMode, Action? onRebuildNeeded)
    {
        var card = CreateCard(r);
        if (card == null) return null;
        r.SetTag(card, $"uprooted-item-{pluginId}");

        var cardContent = r.CreateStackPanel(vertical: true, spacing: 0);
        if (cardContent == null) return card;
        r.SetMargin(cardContent, 16, 14, 16, 14);


        var topRow = r.CreatePanel();
        if (topRow != null)
        {

            var nameText = r.CreateTextBlock(displayName, 14, TextWhite);
            r.SetFontWeightNumeric(nameText, 600);
            ApplyFont(r, nameText, font);
            r.SetHorizontalAlignment(nameText, "Left");
            r.SetVerticalAlignment(nameText, "Center");
            r.SetMargin(nameText, 0, 0, 80, 0);
            r.AddChild(topRow, nameText);


            var rightIcons = r.CreateStackPanel(vertical: false, spacing: 10);
            if (rightIcons != null)
            {
                r.SetHorizontalAlignment(rightIcons, "Right");
                r.SetVerticalAlignment(rightIcons, "Center");


                if (pluginId == "sentry-blocker")
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


                var togglePill = BuildToggleSwitch(r, isEnabled, font, (enabled) =>
                {
                    settings.Plugins[pluginId] = enabled;

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


                    if (filterMode[0] != 0)
                        onRebuildNeeded?.Invoke();
                });
                if (togglePill != null)
                    r.AddChild(rightIcons, togglePill);

                r.AddChild(topRow, rightIcons);
            }

            r.AddChild(cardContent, topRow);
        }


        var descText = r.CreateTextBlock(description, 13, TextMuted);
        if (descText != null)
        {
            ApplyFont(r, descText, font);
            r.SetTextWrapping(descText, "Wrap");
            r.SetMargin(descText, 0, 8, 0, 0);
        }
        r.AddChild(cardContent, descText);

        r.SetBorderChild(card, cardContent);


        var cardBgCurrent = CardBg;
        r.SubscribeEvent(card, "PointerEntered", () =>
            r.SetBackground(card, ColorUtils.Lighten(cardBgCurrent, 4)));
        r.SubscribeEvent(card, "PointerExited", () =>
            r.SetBackground(card, cardBgCurrent));

        return card;
    }

    private static void ShowFilterDropdown(AvaloniaReflection r, object filterBtn,
        object? filterTextBlock, int[] filterMode, Action rebuildGrid, object? font)
    {
        DismissFilterDropdown(r);

        var mainWindow = r.GetMainWindow();
        if (mainWindow == null) return;

        var overlay = r.GetOverlayLayer(mainWindow);
        if (overlay == null) return;

        _filterOverlay = overlay;


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


        _filterPanel = r.CreateBorder(CardBg, 8);
        if (_filterPanel == null) return;
        r.SetTag(_filterPanel, "uprooted-no-recolor");
        SetBorderStroke(r, _filterPanel, CardBorder, 0.5);


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


        double cardW = 480;
        _infoPanel = r.CreateBorder(CardBg, 12);
        if (_infoPanel == null) return;
        r.SetTag(_infoPanel, "uprooted-no-recolor");
        SetBorderStroke(r, _infoPanel, CardBorder, 0.5);
        r.SetWidth(_infoPanel, cardW);


        double cardX = (windowW - cardW) / 2;
        double cardY = windowH * 0.2;
        r.SetCanvasPosition(_infoPanel, cardX, cardY);

        var content = r.CreateStackPanel(vertical: true, spacing: 0);
        if (content == null) return;
        r.SetMargin(content, 24, 20, 24, 20);


        var headerRow = r.CreatePanel();
        if (headerRow != null)
        {
            var titleText = r.CreateTextBlock(pluginName, 18, TextWhite);
            r.SetFontWeightNumeric(titleText, 600);
            ApplyFont(r, titleText, font);
            r.SetHorizontalAlignment(titleText, "Left");
            r.SetVerticalAlignment(titleText, "Center");
            r.AddChild(headerRow, titleText);


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


        var descText = r.CreateTextBlock(description, 13, TextMuted);
        if (descText != null)
        {
            ApplyFont(r, descText, font);
            r.SetTextWrapping(descText, "Wrap");
            r.SetMargin(descText, 0, 14, 0, 0);
        }
        r.AddChild(content, descText);


        if (pluginId == "sentry-blocker")
        {
            var privacyBox = BuildPrivacyInfoBox(r, font);
            if (privacyBox != null)
            {
                r.SetMargin(privacyBox, 0, 14, 0, 0);
                r.AddChild(content, privacyBox);
            }
        }

        r.SetBorderChild(_infoPanel, content);
        r.AddToOverlay(overlay, _infoPanel);
    }

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

    private static object? BuildToggleSwitch(AvaloniaReflection r, bool initialState, object? font,
        Action<bool>? onToggled = null)
    {
        bool state = initialState;

        var dimColor = ColorUtils.Lighten(CardBg, 18);
        var pillColor = state ? AccentGreen : dimColor;


        var pill = r.CreateBorder(pillColor, 12);
        if (pill == null) return null;
        r.SetWidth(pill, 44);
        r.SetHeight(pill, 24);
        r.SetCursorHand(pill);
        r.SetTag(pill, "uprooted-toggle-pill");


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


        var accentColor = AccentGreen;


        r.SubscribeEvent(pill, "PointerPressed", () =>
        {
            state = !state;

            r.SetBackground(pill, state ? accentColor : dimColor);
            if (thumb != null)
                r.SetHorizontalAlignment(thumb, state ? "Right" : "Left");

            onToggled?.Invoke(state);
        });


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


        var pageTitle = r.CreateTextBlock("Themes", 20, TextWhite);
        r.SetFontWeightNumeric(pageTitle, 600);
        ApplyFont(r, pageTitle, font);
        r.AddChild(page, pageTitle);


        var presetHeader = CreateSectionHeader(r, "PRESET THEMES", font);
        if (presetHeader != null)
        {
            r.SetMargin(presetHeader, 0, 20, 0, 12);
            r.AddChild(page, presetHeader);
        }


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


        var customHeader = CreateSectionHeader(r, "CUSTOM THEME", font);
        if (customHeader != null)
        {
            r.SetMargin(customHeader, 0, 16, 0, 12);
            r.AddChild(page, customHeader);
        }

        var customSection = BuildCustomThemeSection(r, settings, font, themeEngine, onThemeChanged);
        if (customSection != null)
            r.AddChild(page, customSection);


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


        var headerRow = r.CreateStackPanel(vertical: false, spacing: 12);
        if (headerRow != null)
        {
            r.SetVerticalAlignment(headerRow, "Center");
            r.SetBackground(headerRow, "Transparent");

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


            r.SetCursorHand(headerRow);
            r.SubscribeEvent(headerRow, "PointerPressed", () =>
            {
                try
                {
                    if (settings.ActiveTheme == "custom") return;

                    Logger.Log("Theme", "Custom theme card clicked");
                    settings.ActiveTheme = "custom";


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



        object?[] accentTextBoxRef = new object?[1];
        object?[] bgTextBoxRef = new object?[1];





        string[] lastAccent = new[] { settings.CustomAccent };
        string[] lastBg = new[] { settings.CustomBackground };


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


        var accentSwatch = r.CreateBorder(settings.CustomAccent, 4);
        var accentRow = BuildColorInputRow(r, "Accent", settings.CustomAccent, font, accentSwatch,
            accentHex =>
            {

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


        var bgSwatch = r.CreateBorder(settings.CustomBackground, 4);
        var bgRow = BuildColorInputRow(r, "Background", settings.CustomBackground, font, bgSwatch,
            bgHex =>
            {

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


        accentTextBoxRef[0] = GetTextBoxFromRow(r, accentRow);
        bgTextBoxRef[0] = GetTextBoxFromRow(r, bgRow);


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

    private static object? BuildColorInputRow(AvaloniaReflection r, string label,
        string initialValue, object? font, object? swatch,
        Action<string>? onColorChanged = null)
    {
        var row = r.CreateStackPanel(vertical: false, spacing: 10);
        if (row == null) return null;
        r.SetVerticalAlignment(row, "Center");


        var labelText = r.CreateTextBlock(label, 13, TextMuted);
        r.SetFontWeightNumeric(labelText, 450);
        ApplyFont(r, labelText, font);
        r.SetWidth(labelText, 90);
        r.AddChild(row, labelText);


        var textBox = r.CreateTextBox("#RRGGBB", initialValue, 7);
        if (textBox != null)
        {
            r.SetWidth(textBox, 100);
            textBox.GetType().GetProperty("FontSize")?.SetValue(textBox, 13.0);
            r.SetBackground(textBox, ColorUtils.Lighten(CardBg, 5));
            r.SetForeground(textBox, TextWhite);
            ApplyFont(r, textBox, font);
            r.SetTag(textBox, "uprooted-color-input");


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


        var outerLayout = r.CreateStackPanel(vertical: true, spacing: 0);
        if (outerLayout == null) return null;
        r.SetMargin(outerLayout, 14, 14, 14, 14);


        var preview = BuildThemePreview(r, bgColor, accentColor);
        if (preview != null)
        {
            r.SetHorizontalAlignment(preview, "Center");
            r.AddChild(outerLayout, preview);
        }


        var bottomRow = r.CreateStackPanel(vertical: false, spacing: 10);
        if (bottomRow != null)
        {
            r.SetMargin(bottomRow, 0, 12, 0, 0);
            r.SetVerticalAlignment(bottomRow, "Center");


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
                Logger.Log("Theme", "Theme switch error: " + ex.Message);
            }
        });


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

    private static object? BuildThemePreview(AvaloniaReflection r, string bgColor, string accentColor)
    {

        var frame = r.CreateBorder("#00000000", 6);
        if (frame == null) return null;
        r.SetTag(frame, "uprooted-no-recolor");
        SetBorderStroke(r, frame, "#30ffffff", 0.5);
        r.SetWidth(frame, 100);
        r.SetHeight(frame, 56);

        var inner = r.CreateStackPanel(vertical: true, spacing: 0);
        if (inner == null) return frame;


        var accentBar = r.CreateBorder(accentColor, 0);
        if (accentBar != null)
        {
            r.SetHeight(accentBar, 6);

            if (r.CornerRadiusType != null)
            {
                var cr = Activator.CreateInstance(r.CornerRadiusType, 5.0, 5.0, 0.0, 0.0);
                accentBar.GetType().GetProperty("CornerRadius")?.SetValue(accentBar, cr);
            }
            r.AddChild(inner, accentBar);
        }


        var body = r.CreateBorder(bgColor, 0);
        if (body != null)
        {
            r.SetHeight(body, 50);
            if (r.CornerRadiusType != null)
            {
                var cr = Activator.CreateInstance(r.CornerRadiusType, 0.0, 0.0, 5.0, 5.0);
                body.GetType().GetProperty("CornerRadius")?.SetValue(body, cr);
            }


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



    private static object? CreateSectionHeader(AvaloniaReflection r, string text, object? font)
    {
        var header = r.CreateTextBlock(text, 12, TextDim);
        r.SetFontWeightNumeric(header, 500);
        ApplyFont(r, header, font);
        return header;
    }

    private static object? CreateCard(AvaloniaReflection r)
    {
        var card = r.CreateBorder(CardBg, 12);
        if (card == null) return null;
        SetBorderStroke(r, card, CardBorder, 0.5);
        return card;
    }

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
