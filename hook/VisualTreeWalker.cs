namespace Uprooted;

internal class SettingsLayout
{
    public required object NavContainer { get; init; }
    public required object ContentArea { get; init; }
    public required object LayoutContainer { get; init; }
    public required object AppSettingsText { get; init; }
    public object? ListBox { get; init; }
    public bool IsGridLayout { get; init; }
    public int ContentColumnIndex { get; init; }
    public int ContentRowIndex { get; init; }

    // New fields for direct injection approach
    public object? BackButton { get; init; }       // The "<" back button/clickable in header row
    public object? VersionBorder { get; init; }     // NavContainer child[1] (version info)
    public object? SignOutControl { get; init; }    // NavContainer child[2] (sign out)
    public object? SidebarGrid { get; init; }       // Grid parent of NavContainer
    public object? SaveBar { get; init; }           // Save bar ("You have unsaved changes" + Revert + Save)
    public int AdvancedIndex { get; init; }         // Index of "Advanced" in ListBox (-1 if not found)
}

/// <summary>
/// Visual tree traversal and settings page layout discovery.
/// </summary>
internal class VisualTreeWalker
{
    private readonly AvaloniaReflection _r;

    public VisualTreeWalker(AvaloniaReflection resolver)
    {
        _r = resolver;
    }

    public IEnumerable<object> DescendantsDepthFirst(object root)
    {
        var stack = new Stack<object>();
        foreach (var child in _r.GetVisualChildren(root))
            stack.Push(child);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in _r.GetVisualChildren(node))
                stack.Push(child);
        }
    }

    public object? FindFirstTextBlock(object root, string exactText)
    {
        foreach (var node in DescendantsDepthFirst(root))
        {
            if (_r.IsTextBlock(node) && _r.GetText(node) == exactText)
                return node;
        }
        return null;
    }

    public object? FindFirstTextBlockContaining(object root, string substring)
    {
        foreach (var node in DescendantsDepthFirst(root))
        {
            if (_r.IsTextBlock(node))
            {
                var text = _r.GetText(node);
                if (text != null && text.Contains(substring, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Discovers the settings page layout by finding "APP SETTINGS" text,
    /// then locating the nav items container and content area.
    /// </summary>
    public SettingsLayout? FindSettingsLayout(object window)
    {
        // Step 1: Find "APP SETTINGS" TextBlock
        var appSettingsText = FindFirstTextBlock(window, "APP SETTINGS");
        appSettingsText ??= FindFirstTextBlock(window, "App Settings");
        if (appSettingsText == null) return null;

        // Step 2: Find the nav items StackPanel (contains the section headers + items)
        var navContainer = FindNavContainer(appSettingsText);
        if (navContainer == null)
        {
            Logger.Log("TreeWalker", "Found 'APP SETTINGS' but could not locate nav container");
            return null;
        }

        var navType = navContainer.GetType().Name;
        var navChildren = _r.GetChildCount(navContainer);
        Logger.Log("TreeWalker", $"Nav container: {navType}, children={navChildren}");

        // Step 2.5: Find the ListBox inside the nav container
        object? listBox = null;
        foreach (var node in DescendantsDepthFirst(navContainer))
        {
            if (node.GetType().Name == "ListBox")
            {
                listBox = node;
                break;
            }
        }

        // Step 3: Find the content area (sibling of the nav container in a layout panel)
        var (layoutContainer, contentArea, isGrid, contentCol, contentRow) = FindContentArea(navContainer);
        if (layoutContainer == null || contentArea == null)
        {
            Logger.Log("TreeWalker", "Could not find content area, using nav container only");
            return new SettingsLayout
            {
                NavContainer = navContainer,
                ContentArea = navContainer,
                LayoutContainer = navContainer,
                AppSettingsText = appSettingsText,
                ListBox = listBox,
                IsGridLayout = false,
                ContentColumnIndex = 0,
                ContentRowIndex = 0
            };
        }

        Logger.Log("TreeWalker", $"Layout: {layoutContainer.GetType().Name}, " +
            $"content: {contentArea.GetType().Name}, isGrid={isGrid}, contentCol={contentCol}");

        // Find additional elements for direct injection
        object? backButton = null;
        object? versionBorder = null;
        object? signOutControl = null;
        object? sidebarGrid = null;
        int advancedIndex = -1;

        if (isGrid && layoutContainer != null)
        {
            backButton = FindBackButton(layoutContainer);
        }

        // NavContainer children: [0]=ListBox, [1]=version Border, [2]=sign-out ContentControl
        int navChildCount = _r.GetChildCount(navContainer);
        if (navChildCount >= 3)
        {
            versionBorder = _r.GetChild(navContainer, 1);
            signOutControl = _r.GetChild(navContainer, 2);
        }

        // SidebarGrid: direct parent of NavContainer (Grid with Rows=[1*,Auto])
        var navParent = _r.GetParent(navContainer);
        if (navParent != null && _r.IsGrid(navParent))
            sidebarGrid = navParent;

        // Find Advanced index in ListBox
        if (listBox != null)
            advancedIndex = FindAdvancedIndex(listBox);

        // Find save bar in settings Grid
        object? saveBar = null;
        if (isGrid && layoutContainer != null)
            saveBar = FindSaveBar(layoutContainer);

        return new SettingsLayout
        {
            NavContainer = navContainer,
            ContentArea = contentArea,
            LayoutContainer = layoutContainer,
            AppSettingsText = appSettingsText,
            ListBox = listBox,
            IsGridLayout = isGrid,
            ContentColumnIndex = contentCol,
            ContentRowIndex = contentRow,
            BackButton = backButton,
            VersionBorder = versionBorder,
            SignOutControl = signOutControl,
            SidebarGrid = sidebarGrid,
            SaveBar = saveBar,
            AdvancedIndex = advancedIndex
        };
    }

    /// <summary>
    /// Search Row=0 of the settings Grid for a TextBlock containing "&lt;" and walk up
    /// to its clickable ancestor (Button or parent with PointerPressed).
    /// </summary>
    public object? FindBackButton(object settingsGrid)
    {
        // Row=0 contains the header bar with the back button
        var children = new List<object>(_r.GetVisualChildren(settingsGrid));
        foreach (var child in children)
        {
            int row = _r.GetGridRow(child);
            if (row != 0) continue;

            // Search this header area for "<" text or a back button
            foreach (var node in DescendantsDepthFirst(child))
            {
                if (!_r.IsTextBlock(node)) continue;
                var text = _r.GetText(node);
                if (text != "<" && text != "\u2190" && text != "\uE72B") continue;

                // Found the "<" text - walk up to find a clickable ancestor
                var current = _r.GetParent(node);
                for (int d = 0; d < 5 && current != null; d++)
                {
                    var typeName = current.GetType().Name;
                    if (typeName == "Button" || typeName.Contains("Button") ||
                        typeName.EndsWith("ClickableControl"))
                    {
                        Logger.Log("TreeWalker", $"Back button found: {typeName} (via '<' text at depth {d})");
                        return current;
                    }
                    current = _r.GetParent(current);
                }

                // No Button ancestor found - use the parent of the text as clickable target
                var textParent = _r.GetParent(node);
                if (textParent != null)
                {
                    Logger.Log("TreeWalker", $"Back button fallback: {textParent.GetType().Name} (parent of '<' text)");
                    return textParent;
                }
            }

            // Fallback: look for any Button-type control in Row=0
            foreach (var node in DescendantsDepthFirst(child))
            {
                var typeName = node.GetType().Name;
                if (typeName == "Button" || typeName.Contains("Button"))
                {
                    Logger.Log("TreeWalker", $"Back button found (Button scan): {typeName}");
                    return node;
                }
            }
        }

        Logger.Log("TreeWalker", "Back button not found in Row=0");
        return null;
    }

    /// <summary>
    /// Search the main settings Grid for the save bar ("You have unsaved changes" + Revert + Save).
    /// Looks at Row=2 (Auto row) first, then falls back to searching all children for "Revert" text.
    /// </summary>
    public object? FindSaveBar(object settingsGrid)
    {
        var children = new List<object>(_r.GetVisualChildren(settingsGrid));

        // Strategy 1: Check children at Grid.Row=2 (the Auto-height row used for save bar)
        foreach (var child in children)
        {
            int row = _r.GetGridRow(child);
            if (row != 2) continue;

            // Verify this contains save-bar-like content (text with "unsaved" or "Revert")
            var unsavedText = FindFirstTextBlockContaining(child, "unsaved");
            var revertText = FindFirstTextBlock(child, "Revert");
            if (unsavedText != null || revertText != null)
            {
                Logger.Log("TreeWalker", $"Save bar found at Row=2: {child.GetType().Name}");
                return child;
            }

            // Row=2 child exists but may not have save bar text yet (bar appears dynamically)
            // Still return it as the save bar container
            var bounds = _r.GetBounds(child);
            if (bounds != null)
            {
                Logger.Log("TreeWalker", $"Save bar candidate at Row=2: {child.GetType().Name} Bounds=({bounds.Value.W:F0}x{bounds.Value.H:F0})");
                return child;
            }
        }

        // Strategy 2: Search all Grid children for one containing "Revert" text
        foreach (var child in children)
        {
            var revertText = FindFirstTextBlock(child, "Revert");
            if (revertText != null)
            {
                Logger.Log("TreeWalker", $"Save bar found via 'Revert' text: {child.GetType().Name} at Row={_r.GetGridRow(child)}");
                return child;
            }
        }

        Logger.Log("TreeWalker", "Save bar not found in settings Grid");
        return null;
    }

    /// <summary>
    /// Find the Revert button inside a save bar container.
    /// Returns the Button (or clickable ancestor) containing "Revert" text.
    /// </summary>
    public object? FindRevertButton(object saveBar)
    {
        foreach (var node in DescendantsDepthFirst(saveBar))
        {
            if (!_r.IsTextBlock(node)) continue;
            if (_r.GetText(node) != "Revert") continue;

            // Walk up to find Button ancestor
            var current = _r.GetParent(node);
            for (int d = 0; d < 5 && current != null; d++)
            {
                var typeName = current.GetType().Name;
                if (typeName == "Button" || typeName.Contains("Button"))
                {
                    Logger.Log("TreeWalker", $"Revert button found: {typeName}");
                    return current;
                }
                current = _r.GetParent(current);
            }

            // Fallback: use parent of text
            var textParent = _r.GetParent(node);
            if (textParent != null)
            {
                Logger.Log("TreeWalker", $"Revert button fallback: {textParent.GetType().Name}");
                return textParent;
            }
        }
        return null;
    }

    /// <summary>
    /// Enumerate ListBox items and return the index of the one containing "Advanced".
    /// Uses the ItemContainerGenerator or visual children to find ListBoxItems.
    /// </summary>
    public int FindAdvancedIndex(object listBox)
    {
        // Get items via visual children of the ListBox's panel
        int index = 0;
        foreach (var node in _r.GetVisualChildren(listBox))
        {
            // The ListBox has a Border > ScrollViewer > Panel > VirtualizingStackPanel > ListBoxItems
            foreach (var item in DescendantsDepthFirst(node))
            {
                var typeName = item.GetType().Name;
                if (typeName != "ListBoxItem") continue;

                // Search this ListBoxItem for a TextBlock with "Advanced"
                foreach (var textNode in DescendantsDepthFirst(item))
                {
                    if (_r.IsTextBlock(textNode) && _r.GetText(textNode) == "Advanced")
                    {
                        Logger.Log("TreeWalker", $"Advanced found at ListBox index {index}");
                        return index;
                    }
                }
                index++;
            }
        }

        Logger.Log("TreeWalker", $"Advanced not found in ListBox ({index} items scanned)");
        return -1;
    }

    /// <summary>
    /// Walk up from "APP SETTINGS" TextBlock to find the StackPanel containing all nav items.
    /// The nav container is a StackPanel with many children that includes section headers
    /// and clickable items.
    /// </summary>
    private object? FindNavContainer(object textBlock)
    {
        var current = _r.GetParent(textBlock);
        int depth = 0;
        object? bestStackPanel = null;

        while (current != null && depth < 15)
        {
            var typeName = current.GetType().Name;

            // Specifically look for StackPanel (not Grid or other Panel types)
            if (typeName == "StackPanel" || typeName.EndsWith("StackPanel"))
            {
                int childCount = _r.GetChildCount(current);
                Logger.Log("TreeWalker", $"  StackPanel at depth {depth}: {childCount} children");

                // The nav StackPanel has many children (section headers + items)
                if (childCount >= 8)
                    return current;

                if (childCount >= 3)
                    bestStackPanel = current;
            }

            // Stop if we hit a ScrollViewer - the StackPanel inside it is what we want
            if (_r.IsScrollViewer(current))
            {
                if (bestStackPanel != null) return bestStackPanel;
            }

            current = _r.GetParent(current);
            depth++;
        }

        return bestStackPanel;
    }

    /// <summary>
    /// From the nav container, walk up to find the layout that holds both nav and content area.
    /// </summary>
    private (object? container, object? content, bool isGrid, int contentCol, int contentRow) FindContentArea(object navContainer)
    {
        var current = _r.GetParent(navContainer);
        int depth = 0;

        while (current != null && depth < 15)
        {
            var children = new List<object>(_r.GetVisualChildren(current));

            if (children.Count >= 2)
            {
                bool isGrid = _r.IsGrid(current);
                int columnCount = 0;

                // Count columns if it's a Grid
                if (isGrid)
                {
                    try
                    {
                        var colDefs = current.GetType().GetProperty("ColumnDefinitions")?.GetValue(current);
                        if (colDefs is System.Collections.IList colList)
                            columnCount = colList.Count;
                    }
                    catch { }
                }

                // Find which child contains our nav container
                int navIndex = -1;
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i] == navContainer || ContainsDescendant(children[i], navContainer))
                    {
                        navIndex = i;
                        break;
                    }
                }

                if (navIndex >= 0)
                {
                    if (isGrid)
                    {
                        var navCol = _r.GetGridColumn(children[navIndex]);
                        var navRow = _r.GetGridRow(children[navIndex]);
                        Logger.Log("TreeWalker", $"Grid @depth {depth}: {children.Count} kids, {columnCount} cols, nav[{navIndex}] Col={navCol} Row={navRow}");
                    }

                    // Skip single-column Grids (vertical stacks) - the real content area
                    // is a multi-column layout (sidebar left, content right)
                    if (isGrid && columnCount <= 1)
                    {
                        Logger.Log("TreeWalker", $"  Skipping single-column Grid at depth {depth}");
                        current = _r.GetParent(current);
                        depth++;
                        continue;
                    }

                    // For multi-column Grids: find content in same row as nav but different column
                    int navGridCol = isGrid ? _r.GetGridColumn(children[navIndex]) : -1;
                    int navGridRow = isGrid ? _r.GetGridRow(children[navIndex]) : -1;

                    // Find the best content candidate: same row as nav, different column, prefer wider columns
                    object? bestContent = null;
                    int bestContentIdx = -1;
                    int bestContentCol = -1;

                    for (int i = 0; i < children.Count; i++)
                    {
                        if (i == navIndex) continue;
                        var child = children[i];
                        var childType = child.GetType().Name;

                        // Skip decorative/utility elements
                        if (childType == "Rectangle" || childType.Contains("Button")) continue;

                        int childCol = isGrid ? _r.GetGridColumn(child) : 0;
                        int childRow = isGrid ? _r.GetGridRow(child) : 0;

                        // Must be in same row as nav, different column
                        if (isGrid && childRow != navGridRow) continue;
                        if (isGrid && childCol == navGridCol) continue;

                        // Content area candidates: Panel, ContentControl, ContentPresenter, ScrollViewer, Border
                        if (childType.Contains("ContentControl") || childType.Contains("ContentPresenter") ||
                            _r.IsScrollViewer(child) || _r.IsPanel(child) || _r.IsBorder(child))
                        {
                            // Prefer higher column index (content is typically to the right of nav)
                            if (bestContent == null || childCol > bestContentCol)
                            {
                                bestContent = child;
                                bestContentIdx = i;
                                bestContentCol = childCol;
                            }
                        }
                    }

                    if (bestContent != null)
                    {
                        int col = isGrid ? _r.GetGridColumn(bestContent) : 0;
                        int row = isGrid ? _r.GetGridRow(bestContent) : 0;
                        Logger.Log("TreeWalker", $"Content found at depth {depth}: nav@{navIndex} (Col={navGridCol},Row={navGridRow}), content@{bestContentIdx} ({bestContent.GetType().Name}), Grid.Col={col}, Grid.Row={row}");
                        return (current, bestContent, isGrid, col, row);
                    }
                }
            }

            current = _r.GetParent(current);
            depth++;
        }

        return (null, null, false, 0, 0);
    }

    private bool ContainsDescendant(object root, object target)
    {
        if (root == target) return true;
        foreach (var child in _r.GetVisualChildren(root))
        {
            if (child == target || ContainsDescendant(child, target))
                return true;
        }
        return false;
    }

    public bool HasTaggedDescendant(object root, string tag)
    {
        foreach (var node in DescendantsDepthFirst(root))
        {
            if (_r.GetTag(node) == tag)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Dump visual tree structure for debugging.
    /// </summary>
    public void DumpTree(object root, int maxDepth = 5, int currentDepth = 0, string indent = "")
    {
        if (currentDepth > maxDepth) return;

        var typeName = root.GetType().Name;
        var text = _r.IsTextBlock(root) ? $" Text=\"{_r.GetText(root)}\"" : "";
        var tag = _r.GetTag(root);
        var tagStr = tag != null ? $" Tag=\"{tag}\"" : "";
        Logger.Log("Tree", $"{indent}{typeName}{text}{tagStr}");

        foreach (var child in _r.GetVisualChildren(root))
        {
            DumpTree(child, maxDepth, currentDepth + 1, indent + "  ");
        }
    }
}
