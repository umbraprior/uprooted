namespace Uprooted;

internal class SidebarInjector
{
    private const string InjectedTag = "uprooted-injected";
    private const int PollIntervalMs = 200;

    private readonly AvaloniaReflection _r;
    private readonly VisualTreeWalker _walker;
    private readonly UprootedSettings _settings;
    private readonly ThemeEngine _themeEngine;
    private Timer? _timer;
    private readonly object _window;


    private object? _listBox;
    private object? _navContainer;
    private object? _contentPanel;
    private object? _sidebarGrid;
    private object? _layoutContainer;


    private List<object> _injectedControls = new();
    private object? _scrollViewerWrapper;
    private object? _originalVersionBorder;
    private object? _originalSignOutControl;
    private int _advancedIndex = -1;


    private object? _activeContentPage;
    private string? _activePage;
    private int _lastListBoxIdx = -1;
    private bool _injected;
    private int _aliveCheckCounter;
    private List<object> _hiddenContentChildren = new();


    private object? _saveBar;
    private object? _revertButton;
    private bool _saveBarWasVisible = true;


    private object? _nativeFontFamily;


    private object? _versionTextBlock;
    private object? _versionContainer;


    private int _injecting;
    private bool _diagnosticsDone;

    public SidebarInjector(AvaloniaReflection resolver, object mainWindow, ThemeEngine themeEngine)
    {
        _r = resolver;
        _walker = new VisualTreeWalker(resolver);
        _settings = UprootedSettings.Load();
        _themeEngine = themeEngine;
        _window = mainWindow;


        if (!_settings.Plugins.ContainsKey("sentry-blocker"))
            _settings.Plugins["sentry-blocker"] = true;
        if (!_settings.Plugins.ContainsKey("themes"))
            _settings.Plugins["themes"] = true;
    }

    public void StartMonitoring()
    {
        Logger.Log("Injector", "Starting settings page monitor (direct injection mode)");
        _timer = new Timer(OnTimerTick, null, PollIntervalMs, PollIntervalMs);
    }

    public void StopMonitoring()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerTick(object? state)
    {
        if (Interlocked.CompareExchange(ref _injecting, 1, 0) != 0) return;
        try
        {
            _r.RunOnUIThread(() =>
            {
                try { CheckAndInject(); }
                catch (Exception ex) { Logger.Log("Injector", $"CheckAndInject error: {ex.Message}"); }
                finally { Interlocked.Exchange(ref _injecting, 0); }
            });

            Task.Delay(3000).ContinueWith(_ =>
            {
                Interlocked.CompareExchange(ref _injecting, 0, 1);
            });
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"OnTimerTick error: {ex.Message}");
            Interlocked.Exchange(ref _injecting, 0);
        }
    }

    private void CheckAndInject()
    {
        if (_injected)
        {

            if (_listBox != null)
            {
                int currentIdx = _r.GetSelectedIndex(_listBox);
                if (currentIdx >= 0 && currentIdx != _lastListBoxIdx)
                {
                    Logger.Log("Injector", $"ListBox selection changed {_lastListBoxIdx} -> {currentIdx}");
                    _lastListBoxIdx = currentIdx;
                    RemoveContentPage();
                }
            }



            _aliveCheckCounter++;
            if (_aliveCheckCounter % 5 == 0)
            {
                var appSettings = _walker.FindFirstTextBlock(_window, "APP SETTINGS")
                    ?? _walker.FindFirstTextBlock(_window, "App Settings");
                if (appSettings == null)
                {
                    Logger.Log("Injector", "Settings page closed (not found in tree), nulling state");
                    NullState();
                }
            }
            return;
        }


        var newLayout = _walker.FindSettingsLayout(_window);
        if (newLayout == null) return;

        Logger.Log("Injector", "Settings page detected, injecting (direct injection mode)");

        if (!_diagnosticsDone)
        {
            _diagnosticsDone = true;
            try { DumpVersionRecon(newLayout); }
            catch (Exception ex) { Logger.Log("Recon", $"DumpVersionRecon error: {ex.Message}"); }
        }

        InjectIntoSettings(newLayout);
    }



    private void InjectIntoSettings(SettingsLayout layout)
    {
        try
        {


            if (_walker.HasTaggedDescendant(layout.NavContainer, InjectedTag))
            {
                Logger.Log("Injector", "Skipping injection: already-injected controls found in NavContainer");

                _navContainer = layout.NavContainer;
                _listBox = layout.ListBox;
                _contentPanel = layout.ContentArea;
                _sidebarGrid = layout.SidebarGrid;
                _lastListBoxIdx = _listBox != null ? _r.GetSelectedIndex(_listBox) : -1;
                _injected = true;
                return;
            }


            _listBox = layout.ListBox;
            _navContainer = layout.NavContainer;
            _contentPanel = layout.ContentArea;
            _sidebarGrid = layout.SidebarGrid;
            _layoutContainer = layout.LayoutContainer;
            _advancedIndex = layout.AdvancedIndex;
            _originalVersionBorder = layout.VersionBorder;
            _originalSignOutControl = layout.SignOutControl;


            _nativeFontFamily = _r.GetFontFamily(layout.AppSettingsText);


            _saveBar = layout.SaveBar;
            if (_saveBar != null)
            {
                _revertButton = _walker.FindRevertButton(_saveBar);
                if (_revertButton != null)
                {
                    _r.SubscribeEvent(_revertButton, "PointerPressed", () =>
                    {
                        Logger.Log("Injector", "Revert button pressed -- cleaning up injection BEFORE teardown");
                        CleanupInjection();
                    });
                    Logger.Log("Injector", $"Revert button PointerPressed subscribed: {_revertButton.GetType().Name}");
                }
                else
                {
                    Logger.Log("Injector", "Save bar found but Revert button not located (may appear later)");
                }
            }


            if (_navContainer != null)
            {
                if (_originalSignOutControl != null)
                    _r.RemoveChild(_navContainer, _originalSignOutControl);
                if (_originalVersionBorder != null)
                    _r.RemoveChild(_navContainer, _originalVersionBorder);
            }


            BuildAndInsertNavItems(layout);


            WrapInScrollViewer();


            InjectVersionText();


            _lastListBoxIdx = _listBox != null ? _r.GetSelectedIndex(_listBox) : -1;

            _injected = true;
            Logger.Log("Injector", $"Injection complete. {_injectedControls.Count} controls added, " +
                $"Advanced at index {_advancedIndex}, ListBox idx={_lastListBoxIdx}");
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"InjectIntoSettings error: {ex}");
            CleanupInjection();
        }
    }

    private void CleanupInjection()
    {
        if (!_injected) return;
        Logger.Log("Injector", "CleanupInjection: removing all injected controls");

        try
        {

            UnwrapScrollViewer();


            if (_navContainer != null)
            {
                foreach (var ctrl in _injectedControls)
                {
                    try { _r.RemoveChild(_navContainer, ctrl); }
                    catch { }
                }
            }


            if (_navContainer != null)
            {
                if (_originalVersionBorder != null)
                    _r.AddChild(_navContainer, _originalVersionBorder);
                if (_originalSignOutControl != null)
                    _r.AddChild(_navContainer, _originalSignOutControl);
            }


            RemoveVersionText();


            if (_saveBar != null)
                _r.SetIsVisible(_saveBar, _saveBarWasVisible);


            RemoveContentPage();
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"CleanupInjection error: {ex.Message}");
        }

        NullState();
    }

    private void NullState()
    {
        _injectedControls.Clear();
        _scrollViewerWrapper = null;
        _listBox = null;
        _navContainer = null;
        _contentPanel = null;
        _sidebarGrid = null;
        _layoutContainer = null;
        _saveBar = null;
        _revertButton = null;
        _nativeFontFamily = null;
        _originalVersionBorder = null;
        _originalSignOutControl = null;
        _activeContentPage = null;
        _activePage = null;
        _hiddenContentChildren.Clear();
        _saveBarWasVisible = true;
        _versionTextBlock = null;
        _versionContainer = null;
        _lastListBoxIdx = -1;
        _injected = false;
        _aliveCheckCounter = 0;
    }



    private void BuildAndInsertNavItems(SettingsLayout layout)
    {
        if (_navContainer == null) return;





        var container = _r.CreateStackPanel(vertical: true, spacing: 0);
        if (container == null) return;
        _r.SetTag(container, InjectedTag);


        var headerFontSize = _r.GetFontSize(layout.AppSettingsText) ?? 11;
        var headerFontWeight = _r.GetFontWeight(layout.AppSettingsText);
        var headerForeground = _r.GetForeground(layout.AppSettingsText);
        var nativeFontFamily = _r.GetFontFamily(layout.AppSettingsText);


        object? nativeNavForeground = null;
        object? nativeNavFontWeight = null;
        if (layout.ListBox != null)
        {

            foreach (var node in _walker.DescendantsDepthFirst(layout.ListBox))
            {
                if (!_r.IsTextBlock(node)) continue;
                var fs = _r.GetFontSize(node);
                if (fs is 14.0)
                {
                    nativeNavForeground = _r.GetForeground(node);
                    nativeNavFontWeight = _r.GetFontWeight(node);
                    Logger.Log("Injector", $"Native nav style: Fg={nativeNavForeground}, FW={nativeNavFontWeight}, Font={_r.GetFontFamily(node)}");
                    break;
                }
            }
        }


        var sectionHeader = BuildSectionHeader("UPROOTED", headerFontSize, headerFontWeight, headerForeground, nativeFontFamily);
        if (sectionHeader != null)
            _r.AddChild(container, sectionHeader);


        foreach (var (label, page) in new[] { ("Uprooted", "uprooted"), ("Plugins", "plugins"), ("Themes", "themes") })
        {
            var item = BuildNavItem(label, page, nativeFontFamily, nativeNavForeground, nativeNavFontWeight);
            if (item != null)
                _r.AddChild(container, item);
        }


        _r.AddChild(_navContainer, container);
        _injectedControls.Add(container);


        if (_originalVersionBorder != null)
        {
            _r.AddChild(_navContainer, _originalVersionBorder);

        }


        if (_originalSignOutControl != null)
        {
            _r.AddChild(_navContainer, _originalSignOutControl);

        }
    }

    private object? BuildSectionHeader(string text, double fontSize, object? fontWeight, object? foreground, object? fontFamily)
    {

        var container = _r.CreateStackPanel(vertical: false, spacing: 0);
        if (container == null) return null;
        _r.SetMargin(container, 12, 12, 12, 0);
        _r.SetTag(container, InjectedTag);

        var header = _r.CreateTextBlock(text, fontSize);
        if (header != null)
        {
            if (fontWeight != null)
                _r.TextBlockType?.GetProperty("FontWeight")?.SetValue(header, fontWeight);
            if (foreground != null)
                _r.TextBlockType?.GetProperty("Foreground")?.SetValue(header, foreground);
            if (fontFamily != null)
                _r.SetFontFamily(header, fontFamily);
            _r.AddChild(container, header);
        }

        return container;
    }

    private object? BuildNavItem(string label, string pageName, object? fontFamily,
        object? nativeForeground = null, object? nativeFontWeight = null)
    {













        var outerPanel = _r.CreatePanel();
        if (outerPanel == null) return null;
        _r.SetTag(outerPanel, $"uprooted-nav-{pageName}");
        _r.SetCursorHand(outerPanel);
        _r.SetHeight(outerPanel, 40);
        _r.SetBackground(outerPanel, "#00000000");


        var innerPanel = _r.CreatePanel();
        if (innerPanel != null)
        {
            _r.SetMargin(innerPanel, 0, 2, 0, 2);


            var highlight = _r.CreateBorder(cornerRadius: 12);
            if (highlight != null)
            {
                _r.SetTag(highlight, $"uprooted-highlight-{pageName}");
                highlight.GetType().GetProperty("Height")?.SetValue(highlight, 36.0);
                _r.AddChild(innerPanel, highlight);
            }


            var textBlock = _r.CreateTextBlock(label, 14);
            if (textBlock != null)
            {

                if (nativeForeground != null)
                    _r.TextBlockType?.GetProperty("Foreground")?.SetValue(textBlock, nativeForeground);
                else
                    _r.SetForeground(textBlock, "#fff2f2f2");

                if (nativeFontWeight != null)
                    _r.TextBlockType?.GetProperty("FontWeight")?.SetValue(textBlock, nativeFontWeight);
                else
                    _r.SetFontWeightNumeric(textBlock, 450);

                _r.SetMargin(textBlock, 12, 0, 12, 0);
                if (fontFamily != null)
                    _r.SetFontFamily(textBlock, fontFamily);
                _r.SetVerticalAlignment(textBlock, "Center");
                _r.AddChild(innerPanel, textBlock);
            }

            _r.AddChild(outerPanel, innerPanel);


            _r.SubscribeEvent(outerPanel, "PointerEntered", () =>
            {
                if (_activePage != pageName && highlight != null)
                    _r.SetBackground(highlight, "#0Dffffff");
            });

            _r.SubscribeEvent(outerPanel, "PointerExited", () =>
            {
                if (_activePage != pageName && highlight != null)
                    _r.SetBackground(highlight, (string?)null);
            });
        }


        _r.SubscribeEvent(outerPanel, "PointerPressed", () =>
        {
            OnNavItemClicked(pageName);
        });

        return outerPanel;
    }



    private void OnNavItemClicked(string pageName)
    {
        try
        {
            Logger.Log("Injector", $"Nav item clicked: {pageName}");
            if (_activePage == pageName) return;


            if (_activeContentPage != null && _contentPanel != null)
            {
                try { _r.RemoveChild(_contentPanel, _activeContentPage); }
                catch { }
            }
            _activeContentPage = null;


            Action rebuildCurrentPage = () =>
            {

                if (_activeContentPage != null && _contentPanel != null)
                {
                    try { _r.RemoveChild(_contentPanel, _activeContentPage); }
                    catch { }
                }
                _activeContentPage = null;
                _activePage = null;
                OnNavItemClicked(pageName);


                _themeEngine.ScheduleWalkBurst();
            };
            var page = ContentPages.BuildPage(pageName, _r, _settings, _nativeFontFamily,
                _themeEngine, rebuildCurrentPage);
            if (page == null)
            {
                Logger.Log("Injector", $"Failed to build page: {pageName}");
                return;
            }


            if (_contentPanel != null)
            {

                _hiddenContentChildren.Clear();
                var children = _r.GetChildren(_contentPanel);
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child != null && child != _activeContentPage)
                        {
                            _r.SetIsVisible(child, false);
                            _hiddenContentChildren.Add(child);
                        }
                    }
                }
                _r.AddChild(_contentPanel, page);
                _activeContentPage = page;
            }


            if (_listBox != null)
            {
                _r.SetSelectedIndex(_listBox, -1);
                _lastListBoxIdx = -1;
            }



            FindAndHideSaveBar();

            _activePage = pageName;
            UpdateNavHighlights();

            if (_contentPanel != null)
                Logger.Log("Injector", $"Content page '{pageName}' displayed in content Panel");
            else
                Logger.Log("Injector", $"Content page '{pageName}' built but contentPanel is null (stale state)");




            ScheduleDelayedSaveBarHide();
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"OnNavItemClicked error: {ex}");
        }
    }

    private void RemoveContentPage()
    {
        if (_activeContentPage != null && _contentPanel != null)
        {
            try { _r.RemoveChild(_contentPanel, _activeContentPage); }
            catch { }
        }
        _activeContentPage = null;
        _activePage = null;


        foreach (var child in _hiddenContentChildren)
        {
            try { _r.SetIsVisible(child, true); }
            catch { }
        }
        _hiddenContentChildren.Clear();


        if (_saveBar != null)
            _r.SetIsVisible(_saveBar, _saveBarWasVisible);

        UpdateNavHighlights();
    }

    private void ClearPanelChildren(object panel)
    {
        var children = _r.GetChildren(panel);
        if (children == null) return;


        for (int i = children.Count - 1; i >= 0; i--)
        {
            try { children.RemoveAt(i); }
            catch { }
        }
    }



    private void WrapInScrollViewer()
    {
        if (_navContainer == null || _sidebarGrid == null) return;

        try
        {

            var scrollViewer = _r.CreateScrollViewer();
            if (scrollViewer == null) return;


            int navRow = _r.GetGridRow(_navContainer);


            _r.RemoveChild(_sidebarGrid, _navContainer);


            _r.SetScrollViewerContent(scrollViewer, _navContainer);


            _r.SetGridRow(scrollViewer, navRow);


            _r.AddChild(_sidebarGrid, scrollViewer);


            scrollViewer.GetType().GetProperty("VerticalScrollBarVisibility")?.SetValue(
                scrollViewer,
                Enum.Parse(scrollViewer.GetType().Assembly.GetType("Avalonia.Controls.Primitives.ScrollBarVisibility")
                    ?? typeof(int), "Auto"));

            _scrollViewerWrapper = scrollViewer;
            Logger.Log("Injector", "NavContainer wrapped in ScrollViewer");
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"WrapInScrollViewer error: {ex.Message}");

            if (_sidebarGrid != null && _navContainer != null)
            {
                try { _r.AddChild(_sidebarGrid, _navContainer); }
                catch { }
            }
        }
    }

    private void UnwrapScrollViewer()
    {
        if (_scrollViewerWrapper == null || _sidebarGrid == null || _navContainer == null) return;

        try
        {
            int navRow = _r.GetGridRow(_scrollViewerWrapper);


            _r.RemoveChild(_sidebarGrid, _scrollViewerWrapper);


            _r.SetScrollViewerContent(_scrollViewerWrapper, null);


            _r.SetGridRow(_navContainer, navRow);
            _r.AddChild(_sidebarGrid, _navContainer);

            _scrollViewerWrapper = null;
            Logger.Log("Injector", "ScrollViewer unwrapped, NavContainer restored");
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"UnwrapScrollViewer error: {ex.Message}");
        }
    }



    private void InjectVersionText()
    {
        if (_sidebarGrid == null) return;

        try
        {

            object? versionStackPanel = null;
            foreach (var child in _r.GetVisualChildren(_sidebarGrid))
            {
                if (_r.GetGridRow(child) != 1) continue;


                foreach (var node in _walker.DescendantsDepthFirst(child))
                {
                    if (!_r.IsTextBlock(node)) continue;
                    var txt = _r.GetText(node);
                    if (txt != null && txt.StartsWith("Root Version", StringComparison.OrdinalIgnoreCase))
                    {
                        versionStackPanel = _r.GetParent(node);
                        break;
                    }
                }
                break;
            }

            if (versionStackPanel == null)
            {
                Logger.Log("Injector", "Version box: could not find 'Root Version' text container");
                return;
            }


            var versionText = _r.CreateTextBlock($"Uprooted {_settings.Version}", 10, "#66f2f2f2");
            if (versionText == null) return;

            _r.AddChild(versionStackPanel, versionText);
            _versionTextBlock = versionText;
            _versionContainer = versionStackPanel;


            var versionButton = FindAncestorOfType(versionStackPanel, "Button");
            if (versionButton != null)
            {
                _r.SubscribeEvent(versionButton, "Click", () =>
                {
                    try
                    {
                        var lines = new List<string>();
                        foreach (var child in _r.GetVisualChildren(versionStackPanel))
                        {
                            if (_r.IsTextBlock(child))
                            {
                                var txt = _r.GetText(child);
                                if (!string.IsNullOrEmpty(txt)) lines.Add(txt);
                            }
                        }
                        if (lines.Count > 0)
                        {
                            _r.CopyToClipboard(_window, string.Join("\n", lines));
                            Logger.Log("Injector", $"Version copy: overwrote clipboard with {lines.Count} lines");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Injector", $"Version copy error: {ex.Message}");
                    }
                });
                Logger.Log("Injector", "Version copy: subscribed to Button.Click");
            }

            Logger.Log("Injector", $"Version box: injected 'Uprooted {_settings.Version}' into version info");
        }
        catch (Exception ex)
        {
            Logger.Log("Injector", $"InjectVersionText error: {ex.Message}");
        }
    }

    private void RemoveVersionText()
    {
        if (_versionTextBlock != null && _versionContainer != null)
        {
            try { _r.RemoveChild(_versionContainer, _versionTextBlock); }
            catch { }
        }
        _versionTextBlock = null;
        _versionContainer = null;
    }



    private object? FindAncestorOfType(object node, string typeName)
    {
        var current = _r.GetParent(node);
        for (int d = 0; d < 10 && current != null; d++)
        {
            if (current.GetType().Name == typeName || current.GetType().Name.Contains(typeName))
                return current;
            current = _r.GetParent(current);
        }
        return null;
    }

    private void FindAndHideSaveBar()
    {
        if (_saveBar == null && _layoutContainer != null)
        {
            _saveBar = _walker.FindSaveBar(_layoutContainer);
            if (_saveBar != null)
            {
                _revertButton = _walker.FindRevertButton(_saveBar);
                if (_revertButton != null)
                {
                    _r.SubscribeEvent(_revertButton, "PointerPressed", () =>
                    {
                        Logger.Log("Injector", "Revert button pressed -- cleaning up injection BEFORE teardown");
                        CleanupInjection();
                    });
                    Logger.Log("Injector", $"Revert button PointerPressed subscribed (late): {_revertButton.GetType().Name}");
                }
            }
        }
        if (_saveBar != null)
        {
            _saveBarWasVisible = _r.GetIsVisible(_saveBar);
            _r.SetIsVisible(_saveBar, false);
        }
    }

    private void ScheduleDelayedSaveBarHide()
    {
        if (_saveBar != null) return;

        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {

            int elapsed = 0;
            foreach (var checkAt in new[] { 200, 500, 1000 })
            {
                int sleepMs = checkAt - elapsed;
                Thread.Sleep(sleepMs);
                elapsed = checkAt;

                if (_saveBar != null || _activePage == null) return;

                _r.RunOnUIThread(() =>
                {
                    try
                    {
                        if (_saveBar != null || _activePage == null) return;
                        FindAndHideSaveBar();
                        if (_saveBar != null)
                            Logger.Log("Injector", "Save bar found+hidden via delayed search (" + checkAt + "ms)");
                    }
                    catch { }
                });
            }
        });
    }

    private void UpdateNavHighlights()
    {
        if (_navContainer == null) return;

        foreach (var node in _walker.DescendantsDepthFirst(_navContainer))
        {
            var tag = _r.GetTag(node);
            if (tag == null || !tag.StartsWith("uprooted-highlight-")) continue;

            var itemPage = tag["uprooted-highlight-".Length..];
            _r.SetBackground(node, itemPage == _activePage ? "#19ffffff" : null);
        }
    }



    private void DumpVersionRecon(SettingsLayout layout)
    {
        Logger.Log("Recon", "=== STYLE RECON ===");


        Logger.Log("Recon", "--- Section header: APP SETTINGS ---");
        var hdr = layout.AppSettingsText;
        Logger.Log("Recon", $"Text: \"{_r.GetText(hdr)}\"");
        Logger.Log("Recon", $"  Type: {hdr.GetType().Name}");
        Logger.Log("Recon", $"  FontSize: {_r.GetFontSize(hdr)}");
        Logger.Log("Recon", $"  FontWeight: {_r.GetFontWeight(hdr)}");
        Logger.Log("Recon", $"  Foreground: {_r.GetForeground(hdr)}");
        Logger.Log("Recon", $"  Margin: {GetPropStr(hdr, "Margin")}");
        Logger.Log("Recon", $"  Bounds: {BoundsStr(_r.GetBounds(hdr))}");

        var p = _r.GetParent(hdr);
        for (int d = 0; d < 6 && p != null; d++)
        {
            var pb = _r.GetBounds(p);
            Logger.Log("Recon", $"  Parent[{d}]: {p.GetType().Name} M={GetPropStr(p, "Margin")} P={GetPropStr(p, "Padding")} Bounds={BoundsStr(pb)}");
            p = _r.GetParent(p);
        }


        if (layout.NavContainer != null)
        {
            var spacingVal = layout.NavContainer.GetType().GetProperty("Spacing")?.GetValue(layout.NavContainer);
            Logger.Log("Recon", $"NavContainer Spacing: {spacingVal}");
        }


        Logger.Log("Recon", "");
        Logger.Log("Recon", "--- ListBox items (first 3 + selected) ---");
        if (layout.ListBox != null)
        {
            var lb = layout.ListBox;
            Logger.Log("Recon", $"ListBox: {lb.GetType().Name} Bounds={BoundsStr(_r.GetBounds(lb))}");
            Logger.Log("Recon", $"  Margin: {GetPropStr(lb, "Margin")}");
            Logger.Log("Recon", $"  Padding: {GetPropStr(lb, "Padding")}");
            Logger.Log("Recon", $"  SelectedIndex: {_r.GetSelectedIndex(lb)}");

            int selectedIdx = _r.GetSelectedIndex(lb);
            int itemIdx = 0;
            foreach (var node in _r.GetVisualChildren(lb))
            {
                foreach (var item in _walker.DescendantsDepthFirst(node))
                {
                    if (item.GetType().Name != "ListBoxItem") continue;
                    bool shouldDump = itemIdx < 3 || itemIdx == selectedIdx;
                    if (shouldDump)
                    {
                        var ib = _r.GetBounds(item);
                        var text = FindFirstTextInTree(item);
                        string sel = itemIdx == selectedIdx ? " [SELECTED]" : "";
                        Logger.Log("Recon", $"");
                        Logger.Log("Recon", $"  ListBoxItem[{itemIdx}] \"{text}\"{sel}");
                        Logger.Log("Recon", $"    Bounds: {BoundsStr(ib)}");
                        Logger.Log("Recon", $"    Margin: {GetPropStr(item, "Margin")}");
                        Logger.Log("Recon", $"    Padding: {GetPropStr(item, "Padding")}");
                        Logger.Log("Recon", $"    MinHeight: {GetPropStr(item, "MinHeight")}");
                        Logger.Log("Recon", $"    Height: {GetPropStr(item, "Height")}");

                        DumpTreeDetailed(item, 2, 10);
                    }
                    itemIdx++;
                }
            }
            Logger.Log("Recon", $"  Total items: {itemIdx}");
        }


        Logger.Log("Recon", "");
        Logger.Log("Recon", "--- ListBox parent chain (for x-offset context) ---");
        if (layout.ListBox != null)
        {
            var lbp = _r.GetParent(layout.ListBox);
            for (int d = 0; d < 6 && lbp != null; d++)
            {
                var lbb = _r.GetBounds(lbp);
                Logger.Log("Recon", $"  Parent[{d}]: {lbp.GetType().Name} M={GetPropStr(lbp, "Margin")} P={GetPropStr(lbp, "Padding")} Bounds={BoundsStr(lbb)}");
                lbp = _r.GetParent(lbp);
            }
        }

        Logger.Log("Recon", "=== END STYLE RECON ===");
    }

    private string BoundsStr((double X, double Y, double W, double H)? b)
    {
        if (b == null) return "null";
        return $"({b.Value.W:F1}x{b.Value.H:F1} @{b.Value.X:F1},{b.Value.Y:F1})";
    }

    private string? FindFirstTextInTree(object root)
    {
        if (_r.IsTextBlock(root))
            return _r.GetText(root);
        foreach (var child in _r.GetVisualChildren(root))
        {
            var text = FindFirstTextInTree(child);
            if (text != null) return text;
        }
        return null;
    }

    private object? FindSidebarBorder(object navContainer)
    {
        var current = _r.GetParent(navContainer);
        int depth = 0;
        while (current != null && depth < 5)
        {
            if (_r.IsBorder(current))
                return current;
            current = _r.GetParent(current);
            depth++;
        }
        return null;
    }

    private string GetPropStr(object? ctrl, string propName)
    {
        if (ctrl == null) return "null";
        try { return ctrl.GetType().GetProperty(propName)?.GetValue(ctrl)?.ToString() ?? "null"; }
        catch { return "err"; }
    }

    private void DumpTreeDetailed(object node, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        var indent = new string(' ', depth * 2);
        var typeName = node.GetType().Name;
        var b = _r.GetBounds(node);
        var props = new List<string> { BoundsStr(b) };


        var m = GetPropStr(node, "Margin");
        var p = GetPropStr(node, "Padding");
        if (m != "0,0,0,0" && m != "0" && m != "null") props.Add($"M={m}");
        if (p != "0,0,0,0" && p != "0" && p != "null") props.Add($"P={p}");


        var w = GetPropStr(node, "Width");
        var h = GetPropStr(node, "Height");
        var minH = GetPropStr(node, "MinHeight");
        if (w != "NaN" && w != "null") props.Add($"W={w}");
        if (h != "NaN" && h != "null") props.Add($"H={h}");
        if (minH != "0" && minH != "null" && minH != "NaN") props.Add($"MinH={minH}");


        try { var bg = node.GetType().GetProperty("Background")?.GetValue(node); if (bg != null) props.Add($"BG={bg}"); } catch { }


        if (_r.IsBorder(node))
        {
            try
            {
                var cr = GetPropStr(node, "CornerRadius");
                var bt = GetPropStr(node, "BorderThickness");
                if (cr != "0" && cr != "0,0,0,0" && cr != "null") props.Add($"CR={cr}");
                if (bt != "0" && bt != "0,0,0,0" && bt != "null") props.Add($"BT={bt}");
            }
            catch { }
        }


        if (_r.IsTextBlock(node))
        {
            props.Add($"Text=\"{_r.GetText(node)}\"");
            props.Add($"FontSize={_r.GetFontSize(node)}");
            props.Add($"FontWeight={_r.GetFontWeight(node)}");
            props.Add($"Fg={_r.GetForeground(node)}");
            var ff = GetPropStr(node, "FontFamily");
            if (ff != "null") props.Add($"FontFamily={ff}");
            var lh = GetPropStr(node, "LineHeight");
            if (lh != "NaN" && lh != "null" && lh != "0") props.Add($"LineHeight={lh}");
        }


        var ha = GetPropStr(node, "HorizontalAlignment");
        var va = GetPropStr(node, "VerticalAlignment");
        if (ha != "Stretch" && ha != "null") props.Add($"HA={ha}");
        if (va != "Stretch" && va != "null") props.Add($"VA={va}");

        Logger.Log("Recon", $"    {indent}{typeName} {string.Join(", ", props)}");

        foreach (var child in _r.GetVisualChildren(node))
            DumpTreeDetailed(child, depth + 1, maxDepth);
    }
}
