namespace Uprooted;

/// <summary>
/// Discord-style HSV color picker popup displayed on the OverlayLayer.
/// Features:
///   - Saturation-Value gradient area (262×180) with draggable circle indicator
///   - Rainbow hue slider with pill-shaped thumb
///   - Hex input field with live preview swatch
/// Singleton: only one popup open at a time.
/// </summary>
internal static class ColorPickerPopup
{
    private static object? _currentOverlay;
    private static object? _currentBackdrop;
    private static object? _currentPopup;

    // HSV state
    private static double _hue = 0;       // 0-360
    private static double _sat = 1.0;     // 0-1
    private static double _val = 1.0;     // 0-1

    // Dragging state
    private static bool _draggingSV;
    private static bool _draggingHue;

    // UI elements that need updating
    private static object? _svArea;          // Grid containing the SV gradient layers
    private static object? _svCursor;        // Ellipse indicator in SV area
    private static object? _svBaseLayer;     // Border with pure hue background
    private static object? _hueSliderGrid;   // Grid for the hue slider
    private static object? _hueThumb;        // Pill-shaped thumb on hue slider
    private static object? _previewSwatch;   // Preview color swatch
    private static object? _hexTextBox;      // Hex input TextBox
    private static object? _linkedTextBox;   // External TextBox to update on color pick
    private static bool _updatingFromHex;    // Prevent recursive hex updates
    private static Action<string>? _onColorChanged; // Live callback on every color change

    // Layout constants - Discord-like proportions
    private const double SV_WIDTH = 262;
    private const double SV_HEIGHT = 180;
    private const double HUE_HEIGHT = 12;
    private const double HUE_THUMB_W = 8;
    private const double HUE_THUMB_H = 18;  // Taller than track for pill look
    private const double POPUP_PADDING = 16;
    private const double SECTION_GAP = 12;
    private const double SV_CURSOR_SIZE = 18;

    // Discord dark popup colors
    private const string POPUP_BG = "#2B2D31";
    private const string POPUP_BORDER = "#1E1F22";
    private const string INPUT_BG = "#1E1F22";
    private const string INPUT_BORDER = "#3F4147";
    private const string TEXT_COLOR = "#B5BAC1";
    private const string TEXT_BRIGHT = "#F2F3F5";

    /// <summary>
    /// Show the color picker popup near the given swatch control.
    /// When a color is picked, it sets the textBox text (which triggers
    /// the existing TextChanged handler to update the swatch preview).
    /// </summary>
    public static void Show(AvaloniaReflection r, object swatch, object? textBox,
        Action<string>? onColorChanged = null)
    {
        Dismiss(r);

        var mainWindow = r.GetMainWindow();
        if (mainWindow == null) return;

        var overlay = r.GetOverlayLayer(mainWindow);
        if (overlay == null) return;

        _currentOverlay = overlay;
        _linkedTextBox = textBox;
        _onColorChanged = onColorChanged;
        _draggingSV = false;
        _draggingHue = false;
        _updatingFromHex = false;

        // Initialize HSV from current textBox value
        var currentHex = r.GetTextBoxText(textBox);
        if (ColorUtils.IsValidHex(currentHex))
        {
            var (h, s, v) = ColorUtils.RgbToHsv(currentHex!);
            _hue = h;
            _sat = s;
            _val = v;
        }
        else
        {
            _hue = 210;
            _sat = 0.75;
            _val = 0.95;
        }

        // Get positioning info
        var swatchBounds = r.GetBounds(swatch);
        var translated = r.TranslatePoint(swatch, 0, 0, overlay);
        if (swatchBounds == null || translated == null) return;

        double swatchX = translated.Value.X;
        double swatchY = translated.Value.Y;
        double swatchW = swatchBounds.Value.W;
        double swatchH = swatchBounds.Value.H;

        var windowBounds = r.GetBounds(mainWindow);
        double windowW = windowBounds?.W ?? 800;
        double windowH = windowBounds?.H ?? 600;

        double popupW = SV_WIDTH + POPUP_PADDING * 2;
        double popupH = SV_HEIGHT + HUE_HEIGHT + 40 + POPUP_PADDING * 2 + SECTION_GAP * 3;

        // Position: right of swatch, or left if near edge
        double popupX = swatchX + swatchW + 8;
        if (popupX + popupW > windowW - 16)
            popupX = swatchX - popupW - 8;

        double popupY = swatchY + (swatchH / 2) - (popupH / 2);
        popupY = Math.Max(8, Math.Min(popupY, windowH - popupH - 8));

        // Backdrop (click to dismiss)
        _currentBackdrop = r.CreateBorder("#01000000", 0);
        if (_currentBackdrop != null)
        {
            r.SetWidth(_currentBackdrop, windowW);
            r.SetHeight(_currentBackdrop, windowH);
            r.SetCanvasPosition(_currentBackdrop, 0, 0);
            r.SetTag(_currentBackdrop, "uprooted-no-recolor");
            r.SubscribeEvent(_currentBackdrop, "PointerPressed", () => Dismiss(r));
            r.AddToOverlay(overlay, _currentBackdrop);
        }

        // Popup container - Discord-style dark card
        _currentPopup = r.CreateBorder(POPUP_BG, 8);
        if (_currentPopup == null) return;
        r.SetTag(_currentPopup, "uprooted-no-recolor");
        SetBorderStroke(r, _currentPopup, POPUP_BORDER, 1.0);
        r.SetCanvasPosition(_currentPopup, popupX, popupY);
        r.AddToOverlay(overlay, _currentPopup);

        var content = r.CreateStackPanel(vertical: true, spacing: SECTION_GAP);
        if (content == null) return;
        r.SetMargin(content, POPUP_PADDING, POPUP_PADDING, POPUP_PADDING, POPUP_PADDING);

        // 1) Saturation-Value gradient area
        var svContainer = BuildSVArea(r);
        if (svContainer != null) r.AddChild(content, svContainer);

        // 2) Hue slider
        var hueSlider = BuildHueSlider(r);
        if (hueSlider != null) r.AddChild(content, hueSlider);

        // 3) Hex input + preview row
        var hexRow = BuildHexRow(r);
        if (hexRow != null) r.AddChild(content, hexRow);

        r.SetBorderChild(_currentPopup, content);

        // Initial state update
        UpdateAllVisuals(r);
    }

    public static void Dismiss(AvaloniaReflection r)
    {
        _draggingSV = false;
        _draggingHue = false;

        if (_currentOverlay == null) return;

        if (_currentBackdrop != null)
        {
            r.RemoveFromOverlay(_currentOverlay, _currentBackdrop);
            _currentBackdrop = null;
        }
        if (_currentPopup != null)
        {
            r.RemoveFromOverlay(_currentOverlay, _currentPopup);
            _currentPopup = null;
        }

        _currentOverlay = null;
        _svArea = null;
        _svCursor = null;
        _svBaseLayer = null;
        _hueSliderGrid = null;
        _hueThumb = null;
        _previewSwatch = null;
        _hexTextBox = null;
        _linkedTextBox = null;
        _onColorChanged = null;
    }

    // ===== Build SV gradient area =====

    private static object? BuildSVArea(AvaloniaReflection r)
    {
        // Outer border with rounded corners and clip
        var svBorder = r.CreateBorder(null, 4);
        if (svBorder == null) return null;
        r.SetTag(svBorder, "uprooted-no-recolor");
        r.SetClipToBounds(svBorder, true);

        // Grid with overlapping layers
        var grid = r.CreateGrid();
        if (grid == null) return null;
        r.SetWidth(grid, SV_WIDTH);
        r.SetHeight(grid, SV_HEIGHT);
        r.SetTag(grid, "uprooted-no-recolor");
        _svArea = grid;

        // Layer 0: solid color = pure hue (base)
        _svBaseLayer = r.CreateBorder(ColorUtils.PureHueHex(_hue), 0);
        if (_svBaseLayer != null)
        {
            r.SetWidth(_svBaseLayer, SV_WIDTH);
            r.SetHeight(_svBaseLayer, SV_HEIGHT);
            r.SetTag(_svBaseLayer, "uprooted-no-recolor");
            r.AddChild(grid, _svBaseLayer);
        }

        // Layer 1: white→transparent (left→right) -- saturation gradient
        var whiteGradient = r.CreateLinearGradientBrush(0, 0, 1, 0, new[]
        {
            ("#FFFFFFFF", 0.0),
            ("#00FFFFFF", 1.0)
        });
        if (whiteGradient != null)
        {
            var whiteLayer = r.CreateBorder(null, 0);
            if (whiteLayer != null)
            {
                r.SetWidth(whiteLayer, SV_WIDTH);
                r.SetHeight(whiteLayer, SV_HEIGHT);
                r.SetBackgroundBrush(whiteLayer, whiteGradient);
                r.SetTag(whiteLayer, "uprooted-no-recolor");
                r.AddChild(grid, whiteLayer);
            }
        }

        // Layer 2: transparent→black (top→bottom) -- value gradient
        var blackGradient = r.CreateLinearGradientBrush(0, 0, 0, 1, new[]
        {
            ("#00000000", 0.0),
            ("#FF000000", 1.0)
        });
        if (blackGradient != null)
        {
            var blackLayer = r.CreateBorder(null, 0);
            if (blackLayer != null)
            {
                r.SetWidth(blackLayer, SV_WIDTH);
                r.SetHeight(blackLayer, SV_HEIGHT);
                r.SetBackgroundBrush(blackLayer, blackGradient);
                r.SetTag(blackLayer, "uprooted-no-recolor");
                r.AddChild(grid, blackLayer);
            }
        }

        // Layer 3: cursor indicator (circle with white border + dark shadow ring)
        _svCursor = r.CreateEllipse(SV_CURSOR_SIZE, SV_CURSOR_SIZE);
        if (_svCursor != null)
        {
            SetBorderStroke(r, _svCursor, "#FFFFFF", 2.5);
            r.SetIsHitTestVisible(_svCursor, false);
            r.SetTag(_svCursor, "uprooted-no-recolor");
            // Add a subtle shadow by setting fill to current color (will be updated)
            r.AddChild(grid, _svCursor);
        }

        // Pointer events on the SV area
        r.SubscribePointerEvent(grid, "PointerPressed", (x, y) =>
        {
            _draggingSV = true;
            HandleSVInput(r, x, y);
        });
        r.SubscribePointerEvent(grid, "PointerMoved", (x, y) =>
        {
            if (_draggingSV) HandleSVInput(r, x, y);
        });
        r.SubscribeEvent(grid, "PointerReleased", () => { _draggingSV = false; });

        r.SetBorderChild(svBorder, grid);
        return svBorder;
    }

    // ===== Build Hue slider =====

    private static object? BuildHueSlider(AvaloniaReflection r)
    {
        // Outer container for centering the thumb vertically
        var container = r.CreateGrid();
        if (container == null) return null;
        r.SetWidth(container, SV_WIDTH);
        r.SetHeight(container, HUE_THUMB_H);
        r.SetTag(container, "uprooted-no-recolor");

        // Track with rounded ends (the rainbow bar)
        var hueBorder = r.CreateBorder(null, HUE_HEIGHT / 2);
        if (hueBorder == null) return null;
        r.SetTag(hueBorder, "uprooted-no-recolor");
        r.SetClipToBounds(hueBorder, true);
        r.SetHeight(hueBorder, HUE_HEIGHT);
        r.SetVerticalAlignment(hueBorder, "Center");

        var hueGrid = r.CreateGrid();
        if (hueGrid == null) return null;
        r.SetWidth(hueGrid, SV_WIDTH);
        r.SetHeight(hueGrid, HUE_HEIGHT);
        r.SetTag(hueGrid, "uprooted-no-recolor");
        _hueSliderGrid = hueGrid;

        // Rainbow gradient background (7-stop spectrum)
        var rainbowBrush = r.CreateLinearGradientBrush(0, 0, 1, 0, new[]
        {
            ("#FFFF0000", 0.0),
            ("#FFFFFF00", 1.0 / 6),
            ("#FF00FF00", 2.0 / 6),
            ("#FF00FFFF", 3.0 / 6),
            ("#FF0000FF", 4.0 / 6),
            ("#FFFF00FF", 5.0 / 6),
            ("#FFFF0000", 1.0)
        });

        var rainbowLayer = r.CreateBorder(null, 0);
        if (rainbowLayer != null && rainbowBrush != null)
        {
            r.SetWidth(rainbowLayer, SV_WIDTH);
            r.SetHeight(rainbowLayer, HUE_HEIGHT);
            r.SetBackgroundBrush(rainbowLayer, rainbowBrush);
            r.SetTag(rainbowLayer, "uprooted-no-recolor");
            r.AddChild(hueGrid, rainbowLayer);
        }

        r.SetBorderChild(hueBorder, hueGrid);
        r.AddChild(container, hueBorder);

        // Pill-shaped thumb indicator (white rounded rectangle)
        _hueThumb = r.CreateBorder("#FFFFFF", HUE_THUMB_W / 2);
        if (_hueThumb != null)
        {
            r.SetWidth(_hueThumb, HUE_THUMB_W);
            r.SetHeight(_hueThumb, HUE_THUMB_H);
            SetBorderStroke(r, _hueThumb, "#20000000", 1);
            r.SetIsHitTestVisible(_hueThumb, false);
            r.SetHorizontalAlignment(_hueThumb, "Left");
            r.SetVerticalAlignment(_hueThumb, "Center");
            r.SetTag(_hueThumb, "uprooted-no-recolor");
            r.AddChild(container, _hueThumb);
        }

        // Pointer events on the full container
        r.SubscribePointerEvent(container, "PointerPressed", (x, y) =>
        {
            _draggingHue = true;
            HandleHueInput(r, x);
        });
        r.SubscribePointerEvent(container, "PointerMoved", (x, y) =>
        {
            if (_draggingHue) HandleHueInput(r, x);
        });
        r.SubscribeEvent(container, "PointerReleased", () => { _draggingHue = false; });

        return container;
    }

    // ===== Build Hex input + preview row =====

    private static object? BuildHexRow(AvaloniaReflection r)
    {
        var row = r.CreateStackPanel(vertical: false, spacing: 10);
        if (row == null) return null;
        r.SetTag(row, "uprooted-no-recolor");

        // Preview swatch (left side, shows current color)
        _previewSwatch = r.CreateBorder(CurrentHex(), 4);
        if (_previewSwatch != null)
        {
            r.SetWidth(_previewSwatch, 36);
            r.SetHeight(_previewSwatch, 36);
            SetBorderStroke(r, _previewSwatch, INPUT_BORDER, 1);
            r.SetTag(_previewSwatch, "uprooted-no-recolor");
            r.AddChild(row, _previewSwatch);
        }

        // Hex label "#"
        var hashLabel = r.CreateTextBlock("#", 14, TEXT_COLOR, "SemiBold");
        if (hashLabel != null)
        {
            r.SetVerticalAlignment(hashLabel, "Center");
            r.SetTag(hashLabel, "uprooted-no-recolor");
            r.AddChild(row, hashLabel);
        }

        // Hex text input (shows value without #)
        var hexValue = CurrentHex().TrimStart('#');
        _hexTextBox = r.CreateTextBox(watermark: "RRGGBB", text: hexValue, maxLength: 6);
        if (_hexTextBox != null)
        {
            r.SetWidth(_hexTextBox, 80);
            r.SetHeight(_hexTextBox, 32);
            r.SetTag(_hexTextBox, "uprooted-no-recolor");
            r.SetBackground(_hexTextBox, INPUT_BG);
            r.SetForeground(_hexTextBox, TEXT_BRIGHT);

            // Subscribe to text changes for bidirectional sync
            r.SubscribeEvent(_hexTextBox, "TextChanged", () =>
            {
                if (_updatingFromHex) return;
                var rawText = r.GetTextBoxText(_hexTextBox);
                if (string.IsNullOrEmpty(rawText)) return;

                // Add # prefix for validation
                var hex = rawText.StartsWith('#') ? rawText : "#" + rawText;
                if (!ColorUtils.IsValidHex(hex)) return;

                _updatingFromHex = true;
                var (h, s, v) = ColorUtils.RgbToHsv(hex);
                _hue = h;
                _sat = s;
                _val = v;
                UpdateAllVisuals(r);
                UpdateLinkedTextBox(r);
                _updatingFromHex = false;
            });

            r.AddChild(row, _hexTextBox);
        }

        return row;
    }

    // ===== Interaction handlers =====

    private static void HandleSVInput(AvaloniaReflection r, double x, double y)
    {
        _sat = Math.Clamp(x / SV_WIDTH, 0, 1);
        _val = 1.0 - Math.Clamp(y / SV_HEIGHT, 0, 1);
        UpdateAllVisuals(r);
        UpdateLinkedTextBox(r);
    }

    private static void HandleHueInput(AvaloniaReflection r, double x)
    {
        _hue = Math.Clamp(x / SV_WIDTH, 0, 1) * 360;
        UpdateAllVisuals(r);
        UpdateLinkedTextBox(r);
    }

    // ===== Visual updates =====

    private static void UpdateAllVisuals(AvaloniaReflection r)
    {
        string hex = CurrentHex();

        // Update SV base layer to pure hue
        if (_svBaseLayer != null)
            r.SetBackground(_svBaseLayer, ColorUtils.PureHueHex(_hue));

        // Position SV cursor (centered on current S,V point)
        if (_svCursor != null)
        {
            double halfSize = SV_CURSOR_SIZE / 2;
            double cx = _sat * SV_WIDTH - halfSize;
            double cy = (1 - _val) * SV_HEIGHT - halfSize;
            r.SetMargin(_svCursor, cx, cy, 0, 0);
            r.SetHorizontalAlignment(_svCursor, "Left");
            r.SetVerticalAlignment(_svCursor, "Top");

            // Fill the cursor with current color for visual feedback
            try
            {
                var fillBrush = r.CreateBrush(hex);
                _svCursor.GetType().GetProperty("Fill")?.SetValue(_svCursor, fillBrush);
            }
            catch { /* Non-critical */ }
        }

        // Position hue thumb
        if (_hueThumb != null)
        {
            double hx = (_hue / 360.0) * SV_WIDTH - HUE_THUMB_W / 2;
            r.SetMargin(_hueThumb, hx, 0, 0, 0);
        }

        // Update preview swatch
        if (_previewSwatch != null)
            r.SetBackground(_previewSwatch, hex);

        // Update hex text (without # prefix, only if not currently being typed)
        if (_hexTextBox != null && !_updatingFromHex)
        {
            _updatingFromHex = true;
            r.SetTextBoxText(_hexTextBox, hex.TrimStart('#'));
            _updatingFromHex = false;
        }
    }

    private static void UpdateLinkedTextBox(AvaloniaReflection r)
    {
        var hex = CurrentHex();
        if (_linkedTextBox != null)
            r.SetTextBoxText(_linkedTextBox, hex);

        // Fire live callback for real-time theme updates
        try { _onColorChanged?.Invoke(hex); }
        catch { /* Non-critical */ }
    }

    private static string CurrentHex() => ColorUtils.HsvToHex(_hue, _sat, _val);

    // ===== Helpers =====

    private static void SetBorderStroke(AvaloniaReflection r, object? element, string hex, double width)
    {
        if (element == null) return;
        var brush = r.CreateBrush(hex);
        element.GetType().GetProperty("BorderBrush")?.SetValue(element, brush);

        if (r.ThicknessType != null)
        {
            // For Ellipse, use StrokeThickness + Stroke instead of BorderThickness
            var strokeThicknessProp = element.GetType().GetProperty("StrokeThickness");
            if (strokeThicknessProp != null)
            {
                strokeThicknessProp.SetValue(element, width);
                element.GetType().GetProperty("Stroke")?.SetValue(element, brush);
                return;
            }

            var thickness = Activator.CreateInstance(r.ThicknessType, width, width, width, width);
            element.GetType().GetProperty("BorderThickness")?.SetValue(element, thickness);
        }
    }
}
