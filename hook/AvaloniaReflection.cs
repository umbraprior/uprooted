using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Uprooted;

internal class AvaloniaReflection
{

    public Type? ApplicationType { get; private set; }
    public Type? DispatcherType { get; private set; }
    public Type? WindowType { get; private set; }
    public Type? ControlType { get; private set; }
    public Type? PanelType { get; private set; }
    public Type? StackPanelType { get; private set; }
    public Type? TextBlockType { get; private set; }
    public Type? BorderType { get; private set; }
    public Type? ScrollViewerType { get; private set; }
    public Type? GridType { get; private set; }
    public Type? ContentControlType { get; private set; }
    public Type? ButtonType { get; private set; }
    public Type? ToggleSwitchType { get; private set; }
    public Type? TextBoxType { get; private set; }
    public Type? EllipseType { get; private set; }
    public Type? CanvasType { get; private set; }


    public Type? OverlayLayerType { get; private set; }
    public Type? PointType { get; private set; }
    public Type? RectType { get; private set; }


    public Type? ResourceDictionaryType { get; private set; }
    public Type? IResourceDictionaryType { get; private set; }


    public Type? SolidColorBrushType { get; private set; }
    public Type? LinearGradientBrushType { get; private set; }
    public Type? GradientStopType { get; private set; }
    public Type? GradientStopsType { get; private set; }
    public Type? RelativePointType { get; private set; }
    public Type? RelativeUnitType { get; private set; }
    public Type? ColorType { get; private set; }
    public Type? ThicknessType { get; private set; }
    public Type? CornerRadiusType { get; private set; }


    public Type? ColumnDefinitionType { get; private set; }
    public Type? GridLengthType { get; private set; }
    public Type? GridUnitTypeEnum { get; private set; }


    public Type? HorizontalAlignmentType { get; private set; }
    public Type? VerticalAlignmentType { get; private set; }
    public Type? OrientationType { get; private set; }
    public Type? TextWrappingType { get; private set; }
    public Type? FontWeightType { get; private set; }
    public Type? CursorType { get; private set; }
    public Type? StandardCursorType { get; private set; }


    public Type? VisualExtensionsType { get; private set; }
    public Type? VisualType { get; private set; }
    public Type? DesktopLifetimeType { get; private set; }
    public Type? TopLevelType { get; private set; }


    private PropertyInfo? _appCurrent;
    private PropertyInfo? _appLifetime;
    private PropertyInfo? _lifetimeMainWindow;
    private PropertyInfo? _lifetimeWindows;
    private PropertyInfo? _dispatcherUIThread;
    private MethodInfo? _dispatcherPost;
    private MethodInfo? _getVisualChildren;
    private MethodInfo? _colorParse;

    private PropertyInfo? _panelChildren;
    private PropertyInfo? _controlTag;
    private PropertyInfo? _controlIsVisible;
    private PropertyInfo? _controlMargin;
    private PropertyInfo? _controlCursor;

    private PropertyInfo? _textBlockText;
    private PropertyInfo? _textBlockFontSize;
    private PropertyInfo? _textBlockFontWeight;
    private PropertyInfo? _textBlockForeground;
    private PropertyInfo? _textBlockTextWrapping;

    private PropertyInfo? _borderChild;
    private PropertyInfo? _borderBackground;
    private PropertyInfo? _borderCornerRadius;
    private PropertyInfo? _borderBorderBrush;
    private PropertyInfo? _borderBorderThickness;

    private PropertyInfo? _scrollViewerContent;
    private PropertyInfo? _stackPanelOrientation;
    private PropertyInfo? _stackPanelSpacing;
    private PropertyInfo? _contentControlContent;

    private MethodInfo? _gridSetColumn;
    private MethodInfo? _gridGetColumn;
    private MethodInfo? _gridSetRow;
    private MethodInfo? _gridGetRow;
    private PropertyInfo? _toggleSwitchIsChecked;
    private FieldInfo? _textBoxTextProperty;
    private FieldInfo? _textBoxWatermarkProperty;
    private FieldInfo? _textBoxMaxLengthProperty;


    private MethodInfo? _overlayGetOverlayLayer;
    private MethodInfo? _canvasSetLeft;
    private MethodInfo? _canvasSetTop;
    private PropertyInfo? _layoutableBounds;
    private MethodInfo? _translatePoint;
    private PropertyInfo? _controlOpacity;
    private PropertyInfo? _controlIsHitTestVisible;


    private PropertyInfo? _appResources;
    private PropertyInfo? _resourcesMergedDicts;

    public bool IsResolved { get; private set; }

    public bool Resolve()
    {
        try
        {
            ResolveTypes();
            ResolveMembers();
            IsResolved = ApplicationType != null && DispatcherType != null && ControlType != null;
            Logger.Log("Reflection", $"Resolved: {IsResolved} " +
                $"(App={ApplicationType != null}, Dispatcher={DispatcherType != null}, " +
                $"Control={ControlType != null}, Panel={PanelType != null}, " +
                $"TextBlock={TextBlockType != null})");
            return IsResolved;
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"Resolve failed: {ex}");
            return false;
        }
    }

    private void ResolveTypes()
    {
        var typeMap = new Dictionary<string, Type>();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var name = asm.GetName().Name ?? "";
                if (!name.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)) continue;

                foreach (var type in asm.GetTypes())
                {
                    var fn = type.FullName;
                    if (fn != null) typeMap[fn] = type;
                }
            }
            catch { }
        }

        Logger.Log("Reflection", $"Scanned Avalonia assemblies, found {typeMap.Count} types");

        Type? Find(string fullName) => typeMap.TryGetValue(fullName, out var t) ? t : null;

        ApplicationType = Find("Avalonia.Application");
        DispatcherType = Find("Avalonia.Threading.Dispatcher");
        WindowType = Find("Avalonia.Controls.Window");
        ControlType = Find("Avalonia.Controls.Control");
        PanelType = Find("Avalonia.Controls.Panel");
        StackPanelType = Find("Avalonia.Controls.StackPanel");
        TextBlockType = Find("Avalonia.Controls.TextBlock");
        BorderType = Find("Avalonia.Controls.Border");
        ScrollViewerType = Find("Avalonia.Controls.ScrollViewer");
        GridType = Find("Avalonia.Controls.Grid");
        ContentControlType = Find("Avalonia.Controls.ContentControl");
        ButtonType = Find("Avalonia.Controls.Button");
        ToggleSwitchType = Find("Avalonia.Controls.ToggleSwitch");
        TextBoxType = Find("Avalonia.Controls.TextBox");
        EllipseType = Find("Avalonia.Controls.Shapes.Ellipse");


        EllipseType ??= typeMap.Values.FirstOrDefault(t =>
            t.Name == "Ellipse" && t.Namespace?.StartsWith("Avalonia") == true && !t.IsAbstract);

        SolidColorBrushType = Find("Avalonia.Media.SolidColorBrush");
        LinearGradientBrushType = Find("Avalonia.Media.LinearGradientBrush");
        GradientStopType = Find("Avalonia.Media.GradientStop");
        GradientStopsType = Find("Avalonia.Media.GradientStops");
        RelativePointType = Find("Avalonia.RelativePoint");
        RelativeUnitType = Find("Avalonia.RelativeUnit");
        ColorType = Find("Avalonia.Media.Color");
        ThicknessType = Find("Avalonia.Thickness");
        CornerRadiusType = Find("Avalonia.CornerRadius");

        ColumnDefinitionType = Find("Avalonia.Controls.ColumnDefinition");
        GridLengthType = Find("Avalonia.Controls.GridLength");
        GridUnitTypeEnum = Find("Avalonia.Controls.GridUnitType");

        HorizontalAlignmentType = Find("Avalonia.Layout.HorizontalAlignment");
        VerticalAlignmentType = Find("Avalonia.Layout.VerticalAlignment");
        OrientationType = Find("Avalonia.Layout.Orientation");
        TextWrappingType = Find("Avalonia.Media.TextWrapping");
        FontWeightType = Find("Avalonia.Media.FontWeight");
        CursorType = Find("Avalonia.Input.Cursor");
        StandardCursorType = Find("Avalonia.Input.StandardCursorType");

        VisualExtensionsType = Find("Avalonia.VisualTree.VisualExtensions");
        VisualType = Find("Avalonia.Visual");
        TopLevelType = Find("Avalonia.Controls.TopLevel");


        OverlayLayerType = Find("Avalonia.Controls.Primitives.OverlayLayer");
        CanvasType = Find("Avalonia.Controls.Canvas");
        PointType = Find("Avalonia.Point");
        RectType = Find("Avalonia.Rect");


        ResourceDictionaryType = Find("Avalonia.Controls.ResourceDictionary");

        IResourceDictionaryType = Find("Avalonia.Controls.IResourceDictionary");


        foreach (var kv in typeMap)
        {
            if (kv.Key.EndsWith("IClassicDesktopStyleApplicationLifetime") && kv.Value.IsInterface)
            {
                DesktopLifetimeType = kv.Value;
                break;
            }
        }

        Logger.Log("Reflection", $"  DesktopLifetime: {(DesktopLifetimeType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  VisualExtensions: {(VisualExtensionsType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  ToggleSwitch: {(ToggleSwitchType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  Ellipse: {(EllipseType != null ? EllipseType.FullName : "MISSING")}");
        Logger.Log("Reflection", $"  Visual: {(VisualType != null ? VisualType.FullName : "MISSING")}");
        Logger.Log("Reflection", $"  OverlayLayer: {(OverlayLayerType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  Canvas: {(CanvasType != null ? "OK" : "MISSING")}");
    }

    private void ResolveMembers()
    {
        var pub = BindingFlags.Public | BindingFlags.Instance;
        var stat = BindingFlags.Public | BindingFlags.Static;


        _appCurrent = ApplicationType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        _appLifetime = ApplicationType?.GetProperty("ApplicationLifetime", pub);


        _lifetimeMainWindow = DesktopLifetimeType?.GetProperty("MainWindow", pub);
        _lifetimeWindows = DesktopLifetimeType?.GetProperty("Windows", pub);


        _dispatcherUIThread = DispatcherType?.GetProperty("UIThread", stat);


        _dispatcherPost = DispatcherType?.GetMethods(pub)
            .Where(m => m.Name == "Post" && !m.IsGenericMethod)
            .FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(Action);
            });


        _dispatcherPost ??= DispatcherType?.GetMethods(pub)
            .Where(m => m.Name == "Post" && !m.IsGenericMethod)
            .FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 1 && p[0].ParameterType == typeof(Action);
            });


        _dispatcherPost ??= DispatcherType?.GetMethods(pub)
            .Where(m => m.Name == "InvokeAsync" && !m.IsGenericMethod)
            .FirstOrDefault(m =>
            {
                var p = m.GetParameters();
                return p.Length == 1 && p[0].ParameterType == typeof(Action);
            });

        Logger.Log("Reflection", $"  Dispatcher.Post: {(_dispatcherPost != null ? _dispatcherPost.Name + "(" + _dispatcherPost.GetParameters().Length + ")" : "MISSING")}");


        _getVisualChildren = VisualExtensionsType?.GetMethods(stat)
            .FirstOrDefault(m => m.Name == "GetVisualChildren" && m.GetParameters().Length == 1);


        _colorParse = ColorType?.GetMethod("Parse", stat, null, new[] { typeof(string) }, null);


        _panelChildren = PanelType?.GetProperty("Children", pub);


        _controlTag = ControlType?.GetProperty("Tag", pub);
        _controlIsVisible = ControlType?.GetProperty("IsVisible", pub);
        _controlMargin = ControlType?.GetProperty("Margin", pub);
        _controlCursor = ControlType?.GetProperty("Cursor", pub);


        _textBlockText = TextBlockType?.GetProperty("Text", pub);
        _textBlockFontSize = TextBlockType?.GetProperty("FontSize", pub);
        _textBlockFontWeight = TextBlockType?.GetProperty("FontWeight", pub);
        _textBlockForeground = TextBlockType?.GetProperty("Foreground", pub);
        _textBlockTextWrapping = TextBlockType?.GetProperty("TextWrapping", pub);


        _borderChild = BorderType?.GetProperty("Child", pub);
        _borderBackground = BorderType?.GetProperty("Background", pub);
        _borderCornerRadius = BorderType?.GetProperty("CornerRadius", pub);
        _borderBorderBrush = BorderType?.GetProperty("BorderBrush", pub);
        _borderBorderThickness = BorderType?.GetProperty("BorderThickness", pub);


        _scrollViewerContent = ScrollViewerType?.GetProperty("Content", pub);


        _stackPanelOrientation = StackPanelType?.GetProperty("Orientation", pub);
        _stackPanelSpacing = StackPanelType?.GetProperty("Spacing", pub);


        _contentControlContent = ContentControlType?.GetProperty("Content", pub);


        _gridSetColumn = GridType?.GetMethod("SetColumn", stat);
        _gridGetColumn = GridType?.GetMethod("GetColumn", stat);
        _gridSetRow = GridType?.GetMethod("SetRow", stat);
        _gridGetRow = GridType?.GetMethod("GetRow", stat);


        _toggleSwitchIsChecked = ToggleSwitchType?.GetProperty("IsChecked", pub);


        var staticPub = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        _textBoxTextProperty = TextBoxType?.GetField("TextProperty", staticPub);
        _textBoxWatermarkProperty = TextBoxType?.GetField("WatermarkProperty", staticPub);
        _textBoxMaxLengthProperty = TextBoxType?.GetField("MaxLengthProperty", staticPub);


        _overlayGetOverlayLayer = OverlayLayerType?.GetMethod("GetOverlayLayer", stat);


        _canvasSetLeft = CanvasType?.GetMethod("SetLeft", stat);
        _canvasSetTop = CanvasType?.GetMethod("SetTop", stat);


        _layoutableBounds = ControlType?.GetProperty("Bounds", pub);




        var translateSearch = ControlType;
        while (translateSearch != null && _translatePoint == null)
        {
            _translatePoint = translateSearch.GetMethods(pub | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == "TranslatePoint" && m.GetParameters().Length == 2);
            translateSearch = translateSearch.BaseType;
        }
        _translatePoint ??= VisualExtensionsType?.GetMethods(stat)
            .FirstOrDefault(m => m.Name == "TranslatePoint" && m.GetParameters().Length == 3);

        if (_translatePoint == null)
        {
            Logger.Log("Reflection", "  TranslatePoint: walking all Avalonia types...");
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name ?? "";
                if (!asmName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.IsAbstract && t.IsSealed)
                        {
                            var m = t.GetMethods(stat)
                                .FirstOrDefault(mi => mi.Name == "TranslatePoint");
                            if (m != null)
                            {
                                Logger.Log("Reflection", $"  TranslatePoint found: {t.FullName}.{m.Name}({m.GetParameters().Length} params, static={m.IsStatic})");
                                _translatePoint = m;
                            }
                        }
                        else
                        {
                            var m = t.GetMethods(pub | BindingFlags.DeclaredOnly)
                                .FirstOrDefault(mi => mi.Name == "TranslatePoint");
                            if (m != null)
                                Logger.Log("Reflection", $"  TranslatePoint found: {t.FullName}.{m.Name}({m.GetParameters().Length} params, static={m.IsStatic})");
                        }
                    }
                }
                catch { }
                if (_translatePoint != null) break;
            }
        }


        _appResources = ApplicationType?.GetProperty("Resources", pub);

        if (IResourceDictionaryType != null)
            _resourcesMergedDicts = IResourceDictionaryType.GetProperty("MergedDictionaries", pub);


        Logger.Log("Reflection", $"  ResourceDictionary: {(ResourceDictionaryType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  IResourceDictionary: {(IResourceDictionaryType != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  App.Resources: {(_appResources != null ? "OK" : "MISSING")}");


        _controlOpacity = ControlType?.GetProperty("Opacity", pub)
            ?? VisualType?.GetProperty("Opacity", pub);
        _controlIsHitTestVisible = ControlType?.GetProperty("IsHitTestVisible", pub);

        Logger.Log("Reflection", $"  OverlayLayer.GetOverlayLayer: {(_overlayGetOverlayLayer != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  Canvas.SetLeft: {(_canvasSetLeft != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  Layoutable.Bounds: {(_layoutableBounds != null ? "OK" : "MISSING")}");
        Logger.Log("Reflection", $"  TranslatePoint: {(_translatePoint != null ? $"OK ({(_translatePoint.IsStatic ? "static" : "instance")}, {_translatePoint.DeclaringType?.Name}.{_translatePoint.Name}({_translatePoint.GetParameters().Length} params))" : "MISSING")}");
    }



    public object? GetAppCurrent() => _appCurrent?.GetValue(null);

    public object? GetMainWindow()
    {
        var app = GetAppCurrent();
        if (app == null) return null;
        var lifetime = _appLifetime?.GetValue(app);
        if (lifetime == null) return null;
        return _lifetimeMainWindow?.GetValue(lifetime);
    }

    public List<object> GetAllWindows()
    {
        var result = new List<object>();
        try
        {
            var app = GetAppCurrent();
            if (app == null) return result;
            var lifetime = _appLifetime?.GetValue(app);
            if (lifetime == null) return result;
            var windows = _lifetimeWindows?.GetValue(lifetime);
            if (windows is IEnumerable enumerable)
            {
                foreach (var w in enumerable)
                {
                    if (w != null) result.Add(w);
                }
            }
        }
        catch { }
        return result;
    }

    private Type? _windowImplType;
    private PropertyInfo? _windowImplOwner;
    private FieldInfo? _windowImplSInstances;
    private bool _windowImplResolved;

    public List<object> GetAllTopLevels()
    {
        if (!_windowImplResolved)
        {
            _windowImplResolved = true;
            ResolveWindowImpl();
        }

        var result = new List<object>();

        if (_windowImplType == null || _windowImplOwner == null || _windowImplSInstances == null)
        {

            var mw = GetMainWindow();
            if (mw != null) result.Add(mw);
            return result;
        }

        try
        {
            var instances = _windowImplSInstances.GetValue(null);
            if (instances is IEnumerable enumerable)
            {
                foreach (var impl in enumerable)
                {
                    if (impl == null) continue;
                    try
                    {
                        var owner = _windowImplOwner.GetValue(impl);
                        if (owner != null && !result.Contains(owner))
                            result.Add(owner);
                    }
                    catch { }
                }
            }
        }
        catch { }

        if (result.Count == 0)
        {
            var mw = GetMainWindow();
            if (mw != null) result.Add(mw);
        }

        return result;
    }

    private void ResolveWindowImpl()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!(asm.FullName?.Contains("Avalonia") == true)) continue;
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (type.Name != "WindowImpl") continue;
                        _windowImplType = type;
                        _windowImplOwner = type.GetProperty("Owner",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        _windowImplSInstances = type.GetField("s_instances",
                            BindingFlags.NonPublic | BindingFlags.Static);

                        Logger.Log("Reflection", "WindowImpl resolved: Owner=" +
                            (_windowImplOwner != null) + " s_instances=" + (_windowImplSInstances != null));
                        return;
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public void RunOnUIThread(Action action)
    {
        var dispatcher = _dispatcherUIThread?.GetValue(null);
        if (dispatcher == null || _dispatcherPost == null)
        {
            action();
            return;
        }

        var pcount = _dispatcherPost.GetParameters().Length;
        if (pcount == 1)
        {
            _dispatcherPost.Invoke(dispatcher, new object[] { action });
        }
        else if (pcount == 2)
        {


            var priorityType = _dispatcherPost.GetParameters()[1].ParameterType;
            object? normalPriority = null;


            var normalProp = priorityType.GetProperty("Normal", BindingFlags.Public | BindingFlags.Static);
            if (normalProp != null)
                normalPriority = normalProp.GetValue(null);


            if (normalPriority == null)
            {
                var normalField = priorityType.GetField("Normal", BindingFlags.Public | BindingFlags.Static);
                if (normalField != null)
                    normalPriority = normalField.GetValue(null);
            }


            if (normalPriority == null && priorityType.IsEnum)
                normalPriority = Enum.Parse(priorityType, "Normal");


            if (normalPriority == null)
                normalPriority = priorityType.IsValueType ? Activator.CreateInstance(priorityType) : null;

            if (normalPriority != null)
                _dispatcherPost.Invoke(dispatcher, new object[] { action, normalPriority });
            else
                action();
        }
        else
        {
            action();
        }
    }

    public IEnumerable<object> GetVisualChildren(object visual)
    {
        if (_getVisualChildren == null) yield break;

        object? result;
        try { result = _getVisualChildren.Invoke(null, new[] { visual }); }
        catch { yield break; }

        if (result is IEnumerable enumerable)
        {
            foreach (var child in enumerable)
            {
                if (child != null) yield return child;
            }
        }
    }

    public IEnumerable<object> GetLogicalChildren(object control)
    {

        var prop = control.GetType().GetProperty("LogicalChildren",
            BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) yield break;

        object? result;
        try { result = prop.GetValue(control); }
        catch { yield break; }

        if (result is IEnumerable enumerable)
        {
            foreach (var child in enumerable)
            {
                if (child != null) yield return child;
            }
        }
    }



    public object? CreateTextBlock(string text, double fontSize = 14, string? foregroundHex = null, string? fontWeight = null)
    {
        if (TextBlockType == null) return null;

        var tb = Activator.CreateInstance(TextBlockType);
        _textBlockText?.SetValue(tb, text);
        _textBlockFontSize?.SetValue(tb, fontSize);

        if (foregroundHex != null)
        {
            var brush = CreateBrush(foregroundHex);
            if (brush != null) _textBlockForeground?.SetValue(tb, brush);
        }

        if (fontWeight != null) SetFontWeight(tb, fontWeight);

        return tb;
    }

    public object? CreateStackPanel(bool vertical = true, double spacing = 0)
    {
        if (StackPanelType == null) return null;

        var sp = Activator.CreateInstance(StackPanelType);

        if (OrientationType != null)
        {
            var orientation = Enum.Parse(OrientationType, vertical ? "Vertical" : "Horizontal");
            _stackPanelOrientation?.SetValue(sp, orientation);
        }

        if (spacing > 0)
            _stackPanelSpacing?.SetValue(sp, spacing);

        return sp;
    }

    public object? CreateBorder(string? bgHex = null, double cornerRadius = 0, object? child = null)
    {
        if (BorderType == null) return null;

        var border = Activator.CreateInstance(BorderType);

        if (bgHex != null)
        {
            var brush = CreateBrush(bgHex);
            if (brush != null) _borderBackground?.SetValue(border, brush);
        }

        if (cornerRadius > 0 && CornerRadiusType != null)
        {
            var cr = Activator.CreateInstance(CornerRadiusType, cornerRadius);
            _borderCornerRadius?.SetValue(border, cr);
        }

        if (child != null)
            _borderChild?.SetValue(border, child);

        return border;
    }

    public object? CreateEllipse(double width, double height, string? fillHex = null)
    {
        if (EllipseType == null) return null;
        try
        {
            var ellipse = Activator.CreateInstance(EllipseType);
            ellipse?.GetType().GetProperty("Width")?.SetValue(ellipse, width);
            ellipse?.GetType().GetProperty("Height")?.SetValue(ellipse, height);
            if (fillHex != null)
            {
                var brush = CreateBrush(fillHex);
                if (brush != null)
                    ellipse?.GetType().GetProperty("Fill")?.SetValue(ellipse, brush);
            }
            return ellipse;
        }
        catch { return null; }
    }

    public object? CreateTextBox(string? watermark = null, string? text = null, int maxLength = 0)
    {
        if (TextBoxType == null) return null;

        try
        {
            var tb = Activator.CreateInstance(TextBoxType);
            if (tb == null) return null;



            if (watermark != null) SetAvaloniaProperty(tb, _textBoxWatermarkProperty, watermark);
            if (text != null) SetAvaloniaProperty(tb, _textBoxTextProperty, text);
            if (maxLength > 0) SetAvaloniaProperty(tb, _textBoxMaxLengthProperty, maxLength);
            return tb;
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"CreateTextBox error: {ex.Message}");
            return null;
        }
    }

    public string? GetTextBoxText(object? textBox)
    {
        if (textBox == null) return null;
        return GetAvaloniaProperty(textBox, _textBoxTextProperty) as string;
    }

    public void SetTextBoxText(object? textBox, string text)
    {
        if (textBox == null) return;
        SetAvaloniaProperty(textBox, _textBoxTextProperty, text);
    }

    private void SetAvaloniaProperty(object control, FieldInfo? avaloniaPropertyField, object value)
    {
        if (avaloniaPropertyField == null) return;
        var avProp = avaloniaPropertyField.GetValue(null);
        if (avProp == null) return;


        EnsureBindingPriorityResolved();


        if (_setValueWithPriority == null && _bindingPriorityStyle != null)
        {
            var methods = control.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "SetValue" && !m.IsGenericMethod && m.GetParameters().Length == 3);
            foreach (var method in methods)
            {
                var parms = method.GetParameters();
                if (parms[0].ParameterType.IsAssignableFrom(avProp.GetType())
                    && parms[2].ParameterType.Name == "BindingPriority")
                {
                    _setValueWithPriority = method;
                    break;
                }
            }
        }

        if (_setValueWithPriority != null && _bindingPriorityStyle != null)
        {
            _setValueWithPriority.Invoke(control, new[] { avProp, value, _bindingPriorityStyle });
        }
        else
        {
            Logger.Log("Reflection", $"SetAvaloniaProperty FAILED: method={_setValueWithPriority != null} priority={_bindingPriorityStyle != null} field={avaloniaPropertyField.Name}");
        }
    }

    private void EnsureBindingPriorityResolved()
    {
        if (_priorityResolved) return;
        _priorityResolved = true;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var bpType = asm.GetType("Avalonia.Data.BindingPriority");
            if (bpType != null)
            {
                _bindingPriorityStyle = Enum.ToObject(bpType, 0);
                break;
            }
        }
    }

    private object? GetAvaloniaProperty(object control, FieldInfo? avaloniaPropertyField)
    {
        if (avaloniaPropertyField == null) return null;
        var avProp = avaloniaPropertyField.GetValue(null);
        if (avProp == null) return null;


        var methods = control.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == "GetValue" && !m.IsGenericMethod && m.GetParameters().Length == 1);
        foreach (var method in methods)
        {
            var parms = method.GetParameters();
            if (parms[0].ParameterType.IsAssignableFrom(avProp.GetType()))
            {
                return method.Invoke(control, new[] { avProp });
            }
        }
        return null;
    }

    public object? CreatePanel()
    {
        if (PanelType == null) return null;
        return Activator.CreateInstance(PanelType);
    }

    public object? CreateScrollViewer(object? content = null)
    {
        if (ScrollViewerType == null) return null;

        var sv = Activator.CreateInstance(ScrollViewerType);
        if (content != null)
            _scrollViewerContent?.SetValue(sv, content);
        return sv;
    }

    public object? CreateBrush(string hex)
    {
        if (_colorParse == null || SolidColorBrushType == null) return null;

        try
        {
            var color = _colorParse.Invoke(null, new object[] { hex });


            var brush = Activator.CreateInstance(SolidColorBrushType);
            SolidColorBrushType.GetProperty("Color")?.SetValue(brush, color);
            return brush;
        }
        catch { return null; }
    }



    public void SetMargin(object? control, double left, double top, double right, double bottom)
    {
        if (control == null || ThicknessType == null) return;
        var thickness = Activator.CreateInstance(ThicknessType, left, top, right, bottom);
        _controlMargin?.SetValue(control, thickness);
    }

    public void SetPadding(object? control, double left, double top, double right, double bottom)
    {
        if (control == null || ThicknessType == null) return;
        var thickness = Activator.CreateInstance(ThicknessType, left, top, right, bottom);

        var paddingProp = control.GetType().GetProperty("Padding");
        paddingProp?.SetValue(control, thickness);
    }

    public void SetTag(object? control, string tag) => _controlTag?.SetValue(control, tag);
    public string? GetTag(object? control) => _controlTag?.GetValue(control) as string;

    public void SetIsVisible(object? control, bool visible) => _controlIsVisible?.SetValue(control, visible);
    public bool GetIsVisible(object? control) => _controlIsVisible?.GetValue(control) is true;

    public void SetBackground(object? control, string? hex)
    {
        if (control == null) return;
        var bgProp = control.GetType().GetProperty("Background");
        if (bgProp == null) return;

        if (hex == null)
        {
            bgProp.SetValue(control, null);
            return;
        }

        var brush = CreateBrush(hex);
        if (brush != null) bgProp.SetValue(control, brush);
    }

    public void SetForeground(object? control, string hex)
    {
        if (control == null) return;
        var brush = CreateBrush(hex);
        if (brush == null) return;
        var fgProp = control.GetType().GetProperty("Foreground");
        fgProp?.SetValue(control, brush);
    }

    public void SetFontWeight(object? control, string weight)
    {
        if (control == null || FontWeightType == null) return;


        var weightProp = FontWeightType.GetProperty(weight, BindingFlags.Public | BindingFlags.Static);
        if (weightProp != null)
        {
            _textBlockFontWeight?.SetValue(control, weightProp.GetValue(null));
            return;
        }


        var weightField = FontWeightType.GetField(weight, BindingFlags.Public | BindingFlags.Static);
        if (weightField != null)
        {
            _textBlockFontWeight?.SetValue(control, weightField.GetValue(null));
            return;
        }


        try
        {
            var parsed = Enum.Parse(FontWeightType, weight);
            _textBlockFontWeight?.SetValue(control, parsed);
        }
        catch { }
    }

    public void SetFontWeightNumeric(object? control, int weight)
    {
        if (control == null || FontWeightType == null) return;
        try
        {
            var fw = Activator.CreateInstance(FontWeightType, weight);
            _textBlockFontWeight?.SetValue(control, fw);
        }
        catch { }
    }

    public void SetTextWrapping(object? control, string wrapping)
    {
        if (control == null || TextWrappingType == null) return;
        try
        {
            var val = Enum.Parse(TextWrappingType, wrapping);
            _textBlockTextWrapping?.SetValue(control, val);
        }
        catch { }
    }

    public void SetHorizontalAlignment(object? control, string alignment)
    {
        if (control == null || HorizontalAlignmentType == null) return;
        try
        {
            var val = Enum.Parse(HorizontalAlignmentType, alignment);
            control.GetType().GetProperty("HorizontalAlignment")?.SetValue(control, val);
        }
        catch { }
    }

    public void SetVerticalAlignment(object? control, string alignment)
    {
        if (control == null || VerticalAlignmentType == null) return;
        try
        {
            var val = Enum.Parse(VerticalAlignmentType, alignment);
            control.GetType().GetProperty("VerticalAlignment")?.SetValue(control, val);
        }
        catch { }
    }

    public void SetCursorHand(object? control)
    {
        if (control == null || CursorType == null || StandardCursorType == null) return;
        try
        {
            var hand = Enum.Parse(StandardCursorType, "Hand");
            var cursor = Activator.CreateInstance(CursorType, hand);
            _controlCursor?.SetValue(control, cursor);
        }
        catch { }
    }



    public string? GetText(object? control) => _textBlockText?.GetValue(control) as string;
    public double? GetFontSize(object? control) => _textBlockFontSize?.GetValue(control) as double?;
    public object? GetFontWeight(object? control) => _textBlockFontWeight?.GetValue(control);
    public object? GetForeground(object? control) => _textBlockForeground?.GetValue(control);
    public object? GetFontFamily(object? control)
    {
        if (control == null) return null;
        return control.GetType().GetProperty("FontFamily")?.GetValue(control);
    }

    public void SetFontFamily(object? control, object? fontFamily)
    {
        if (control == null || fontFamily == null) return;
        try
        {
            control.GetType().GetProperty("FontFamily")?.SetValue(control, fontFamily);
        }
        catch { }
    }



    public IList? GetChildren(object? panel)
    {
        if (panel == null) return null;
        return _panelChildren?.GetValue(panel) as IList;
    }

    public void AddChild(object? panel, object? child)
    {
        if (panel == null || child == null) return;
        GetChildren(panel)?.Add(child);
    }

    public void InsertChild(object? panel, int index, object? child)
    {
        if (panel == null || child == null) return;
        GetChildren(panel)?.Insert(index, child);
    }

    public int GetChildCount(object? panel) => GetChildren(panel)?.Count ?? 0;

    public object? GetChild(object? panel, int index)
    {
        var children = GetChildren(panel);
        if (children == null || index < 0 || index >= children.Count) return null;
        return children[index];
    }

    public void RemoveChild(object? panel, object? child)
    {
        if (panel == null || child == null) return;
        GetChildren(panel)?.Remove(child);
    }

    public void SetBorderChild(object? border, object? child) => _borderChild?.SetValue(border, child);
    public object? GetBorderChild(object? border) => _borderChild?.GetValue(border);

    public void SetScrollViewerContent(object? sv, object? content) => _scrollViewerContent?.SetValue(sv, content);

    public void SetGridColumn(object? control, int column) => _gridSetColumn?.Invoke(null, new[] { control, (object)column });
    public int GetGridColumn(object? control)
    {
        if (control == null || _gridGetColumn == null) return 0;
        try { return (int)_gridGetColumn.Invoke(null, new[] { control })!; }
        catch { return 0; }
    }
    public void SetGridRow(object? control, int row) => _gridSetRow?.Invoke(null, new[] { control, (object)row });
    public int GetGridRow(object? control)
    {
        if (control == null || _gridGetRow == null) return 0;
        try { return (int)_gridGetRow.Invoke(null, new[] { control })!; }
        catch { return 0; }
    }



    public object? CreateGrid()
    {
        if (GridType == null) return null;
        return Activator.CreateInstance(GridType);
    }

    public void AddGridColumn(object? grid, double starWidth = 1.0)
    {
        if (grid == null || ColumnDefinitionType == null || GridLengthType == null || GridUnitTypeEnum == null) return;
        try
        {

            var starUnit = Enum.Parse(GridUnitTypeEnum, "Star");

            var gridLength = Activator.CreateInstance(GridLengthType, starWidth, starUnit);

            var colDef = Activator.CreateInstance(ColumnDefinitionType);
            ColumnDefinitionType.GetProperty("Width")?.SetValue(colDef, gridLength);


            var colDefs = grid.GetType().GetProperty("ColumnDefinitions")?.GetValue(grid);
            if (colDefs is System.Collections.IList colList)
                colList.Add(colDef);
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"AddGridColumn error: {ex.Message}");
        }
    }



    public bool IsTextBlock(object? obj) => obj != null && TextBlockType?.IsAssignableFrom(obj.GetType()) == true;
    public bool IsPanel(object? obj) => obj != null && PanelType?.IsAssignableFrom(obj.GetType()) == true;
    public bool IsBorder(object? obj) => obj != null && BorderType?.IsAssignableFrom(obj.GetType()) == true;
    public bool IsGrid(object? obj) => obj != null && GridType?.IsAssignableFrom(obj.GetType()) == true;
    public bool IsScrollViewer(object? obj) => obj != null && ScrollViewerType?.IsAssignableFrom(obj.GetType()) == true;



    public void SubscribeEvent(object control, string eventName, Action callback)
    {
        try
        {
            var type = control.GetType();
            var eventInfo = type.GetEvent(eventName);
            if (eventInfo == null)
            {
                Logger.Log("Reflection", $"Event '{eventName}' not found on {type.Name}");
                return;
            }

            var handlerType = eventInfo.EventHandlerType!;
            var invokeMethod = handlerType.GetMethod("Invoke")!;
            var paramTypes = invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();

            var p0 = Expression.Parameter(paramTypes[0], "sender");
            var p1 = Expression.Parameter(paramTypes[1], "e");
            var callbackExpr = Expression.Constant(callback);
            var invokeExpr = Expression.Invoke(callbackExpr);
            var lambda = Expression.Lambda(handlerType, invokeExpr, p0, p1);
            var handler = lambda.Compile();

            eventInfo.AddEventHandler(control, handler);
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"SubscribeEvent({eventName}) failed: {ex.Message}");
        }
    }



    public bool ClearValue(object? control, string propertyFieldName)
    {
        if (control == null) return false;
        try
        {

            var field = control.GetType().GetField(propertyFieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field == null)
            {
                Logger.Log("Reflection", $"ClearValue: field '{propertyFieldName}' not found on {control.GetType().Name}");
                return false;
            }

            var avaloniaProperty = field.GetValue(null);
            if (avaloniaProperty == null) return false;


            var clearMethods = control.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "ClearValue" && !m.IsGenericMethod && m.GetParameters().Length == 1);

            foreach (var method in clearMethods)
            {
                var paramType = method.GetParameters()[0].ParameterType;
                if (paramType.IsAssignableFrom(avaloniaProperty.GetType()))
                {
                    method.Invoke(control, new[] { avaloniaProperty });
                    Logger.Log("Reflection", $"ClearValue({propertyFieldName}) succeeded on {control.GetType().Name}");
                    return true;
                }
            }

            Logger.Log("Reflection", $"ClearValue: no matching ClearValue overload for {avaloniaProperty.GetType().Name}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"ClearValue error: {ex.Message}");
            return false;
        }
    }

    public bool ClearValueSilent(object? control, string propertyFieldName)
    {
        if (control == null) return false;
        try
        {
            var field = control.GetType().GetField(propertyFieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field == null) return false;

            var avaloniaProperty = field.GetValue(null);
            if (avaloniaProperty == null) return false;

            var clearMethods = control.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "ClearValue" && !m.IsGenericMethod && m.GetParameters().Length == 1);

            foreach (var method in clearMethods)
            {
                var paramType = method.GetParameters()[0].ParameterType;
                if (paramType.IsAssignableFrom(avaloniaProperty.GetType()))
                {
                    method.Invoke(control, new[] { avaloniaProperty });
                    return true;
                }
            }
            return false;
        }
        catch { return false; }
    }



    private System.Reflection.MethodInfo? _setValueWithPriority;
    private object? _bindingPriorityStyle;
    private bool _priorityResolved;

    public bool SetValueStylePriority(object control, string propertyFieldName, object? value)
    {
        try
        {
            EnsureBindingPriorityResolved();
            if (_bindingPriorityStyle == null) return false;


            var field = control.GetType().GetField(propertyFieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field == null) return false;

            var avaloniaProperty = field.GetValue(null);
            if (avaloniaProperty == null) return false;


            if (_setValueWithPriority == null)
            {
                var methods = control.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "SetValue" && !m.IsGenericMethod && m.GetParameters().Length == 3);
                foreach (var method in methods)
                {
                    var parms = method.GetParameters();
                    if (parms[0].ParameterType.IsAssignableFrom(avaloniaProperty.GetType())
                        && parms[2].ParameterType.Name == "BindingPriority")
                    {
                        _setValueWithPriority = method;
                        break;
                    }
                }
            }

            if (_setValueWithPriority != null)
            {
                _setValueWithPriority.Invoke(control, new[] { avaloniaProperty, value, _bindingPriorityStyle });
                return true;
            }

            return false;
        }
        catch { return false; }
    }

    public static string PropertyToFieldName(string propertyName) => propertyName + "Property";



    public void SetMaxHeight(object? control, double maxHeight)
    {
        if (control == null) return;
        control.GetType().GetProperty("MaxHeight")?.SetValue(control, maxHeight);
    }

    public double GetMaxHeight(object? control)
    {
        if (control == null) return double.NaN;
        return control.GetType().GetProperty("MaxHeight")?.GetValue(control) as double? ?? double.NaN;
    }

    public void ClearMaxHeight(object? control)
    {
        if (control == null) return;
        control.GetType().GetProperty("MaxHeight")?.SetValue(control, double.NaN);
    }



    public object? GetContent(object? contentControl)
    {
        if (contentControl == null) return null;
        return _contentControlContent?.GetValue(contentControl)
            ?? contentControl.GetType().GetProperty("Content")?.GetValue(contentControl);
    }

    public void SetContent(object? contentControl, object? content)
    {
        if (contentControl == null) return;
        if (_contentControlContent != null)
            _contentControlContent.SetValue(contentControl, content);
        else
            contentControl.GetType().GetProperty("Content")?.SetValue(contentControl, content);
    }



    public int GetSelectedIndex(object? listBox)
    {
        if (listBox == null) return -1;
        var prop = listBox.GetType().GetProperty("SelectedIndex");
        return prop?.GetValue(listBox) as int? ?? -1;
    }

    public void SetSelectedIndex(object? listBox, int index)
    {
        if (listBox == null) return;
        var prop = listBox.GetType().GetProperty("SelectedIndex");
        prop?.SetValue(listBox, index);
    }



    public void CopyToClipboard(object window, string text)
    {
        try
        {

            var clipboardProp = window.GetType().GetProperty("Clipboard");
            var clipboard = clipboardProp?.GetValue(window);
            if (clipboard == null)
            {
                Logger.Log("Reflection", "Clipboard property not found on window");
                return;
            }
            var setTextAsync = clipboard.GetType().GetMethod("SetTextAsync", new[] { typeof(string) });
            if (setTextAsync != null)
            {
                setTextAsync.Invoke(clipboard, new object[] { text });
                Logger.Log("Reflection", $"Copied to clipboard: {text}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"CopyToClipboard error: {ex.Message}");
        }
    }



    public object? GetParent(object? node)
    {
        if (node == null) return null;
        var type = node.GetType();


        var vpProp = type.GetProperty("VisualParent");
        if (vpProp != null) return vpProp.GetValue(node);

        var pProp = type.GetProperty("Parent");
        return pProp?.GetValue(node);
    }



    public object? GetOverlayLayer(object visual)
    {
        if (_overlayGetOverlayLayer == null) return null;
        try
        {
            return _overlayGetOverlayLayer.Invoke(null, new[] { visual });
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"GetOverlayLayer error: {ex.Message}");
            return null;
        }
    }

    public void SetCanvasPosition(object? control, double left, double top)
    {
        if (control == null) return;
        try
        {
            _canvasSetLeft?.Invoke(null, new[] { control, (object)left });
            _canvasSetTop?.Invoke(null, new[] { control, (object)top });
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"SetCanvasPosition error: {ex.Message}");
        }
    }

    public (double X, double Y, double W, double H)? GetBounds(object? control)
    {
        if (control == null || _layoutableBounds == null) return null;
        try
        {
            var bounds = _layoutableBounds.GetValue(control);
            if (bounds == null) return null;
            var bt = bounds.GetType();
            var x = (double)(bt.GetProperty("X")?.GetValue(bounds) ?? 0.0);
            var y = (double)(bt.GetProperty("Y")?.GetValue(bounds) ?? 0.0);
            var w = (double)(bt.GetProperty("Width")?.GetValue(bounds) ?? 0.0);
            var h = (double)(bt.GetProperty("Height")?.GetValue(bounds) ?? 0.0);
            return (x, y, w, h);
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"GetBounds error: {ex.Message}");
            return null;
        }
    }

    public (double X, double Y)? TranslatePoint(object from, double x, double y, object to)
    {
        if (_translatePoint == null || PointType == null) return null;
        try
        {
            var point = Activator.CreateInstance(PointType, x, y);

            object? result;
            if (_translatePoint.IsStatic)
            {

                result = _translatePoint.Invoke(null, new[] { from, point, to });
            }
            else
            {

                result = _translatePoint.Invoke(from, new[] { point, to });
            }

            if (result == null) return null;


            var resultType = result.GetType();


            if (resultType.IsGenericType && resultType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var hasValue = (bool)(resultType.GetProperty("HasValue")?.GetValue(result) ?? false);
                if (!hasValue) return null;
                result = resultType.GetProperty("Value")?.GetValue(result);
                if (result == null) return null;
                resultType = result.GetType();
            }

            var rx = (double)(resultType.GetProperty("X")?.GetValue(result) ?? 0.0);
            var ry = (double)(resultType.GetProperty("Y")?.GetValue(result) ?? 0.0);
            return (rx, ry);
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"TranslatePoint error: {ex.Message}");
            return null;
        }
    }

    public void AddToOverlay(object? overlay, object? child)
    {
        if (overlay == null || child == null) return;
        GetChildren(overlay)?.Add(child);
    }

    public void RemoveFromOverlay(object? overlay, object? child)
    {
        if (overlay == null || child == null) return;
        try { GetChildren(overlay)?.Remove(child); }
        catch (Exception ex) { Logger.Log("Reflection", $"RemoveFromOverlay error: {ex.Message}"); }
    }



    public void SetWidth(object? control, double width)
    {
        if (control == null) return;
        control.GetType().GetProperty("Width")?.SetValue(control, width);
    }

    public void SetHeight(object? control, double height)
    {
        if (control == null) return;
        control.GetType().GetProperty("Height")?.SetValue(control, height);
    }

    public void SetOpacity(object? control, double opacity)
    {
        if (control == null) return;
        _controlOpacity?.SetValue(control, opacity);
    }

    public void SetIsHitTestVisible(object? control, bool visible)
    {
        if (control == null) return;
        _controlIsHitTestVisible?.SetValue(control, visible);
    }

    public object? CreateCanvas()
    {
        if (CanvasType == null) return null;
        return Activator.CreateInstance(CanvasType);
    }



    public object? GetAppResources()
    {
        var app = GetAppCurrent();
        if (app == null || _appResources == null) return null;
        return _appResources.GetValue(app);
    }

    public object? GetStyleResources(int styleIndex)
    {
        var app = GetAppCurrent();
        if (app == null) return null;
        try
        {
            var stylesProp = app.GetType().GetProperty("Styles");
            var styles = stylesProp?.GetValue(app);
            if (styles is IEnumerable styleEnum)
            {
                int i = 0;
                foreach (var style in styleEnum)
                {
                    if (i == styleIndex && style != null)
                    {
                        var resProp = style.GetType().GetProperty("Resources");
                        return resProp?.GetValue(style);
                    }
                    i++;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", "GetStyleResources error: " + ex.Message);
        }
        return null;
    }

    public object? GetResource(object? dict, string key)
    {
        if (dict == null) return null;
        try
        {

            var indexer = dict.GetType().GetProperty("Item",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, null, new[] { typeof(object) }, null);
            if (indexer != null)
            {
                return indexer.GetValue(dict, new object[] { key });
            }
        }
        catch { }
        return null;
    }

    public IList? GetMergedDictionaries(object? resources)
    {
        if (resources == null) return null;

        if (_resourcesMergedDicts != null)
        {
            try { return _resourcesMergedDicts.GetValue(resources) as IList; }
            catch { }
        }

        var prop = resources.GetType().GetProperty("MergedDictionaries",
            BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(resources) as IList;
    }

    public object? CreateResourceDictionary()
    {
        if (ResourceDictionaryType == null) return null;
        return Activator.CreateInstance(ResourceDictionaryType);
    }

    public void AddResource(object? dict, string key, object? value)
    {
        if (dict == null || value == null) return;
        try
        {


            var indexer = dict.GetType().GetProperty("Item",
                BindingFlags.Public | BindingFlags.Instance,
                null, null, new[] { typeof(object) }, null);
            if (indexer != null)
            {
                indexer.SetValue(dict, value, new object[] { key });
                return;
            }


            var addMethod = dict.GetType().GetMethod("Add",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(object), typeof(object) }, null);
            addMethod?.Invoke(dict, new[] { (object)key, value });
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"AddResource({key}) error: {ex.Message}");
        }
    }

    public bool RemoveResource(object? dict, string key)
    {
        if (dict == null) return false;
        try
        {
            var removeMethod = dict.GetType().GetMethod("Remove",
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(object) }, null);
            if (removeMethod != null)
            {
                removeMethod.Invoke(dict, new object[] { key });
                return true;
            }
        }
        catch { }
        return false;
    }

    public object? ParseColor(string hex)
    {
        if (_colorParse == null) return null;
        try
        {
            return _colorParse.Invoke(null, new object[] { hex });
        }
        catch { return null; }
    }



    public object? CreateLinearGradientBrush(double startX, double startY, double endX, double endY,
        (string hex, double offset)[] stops)
    {
        if (LinearGradientBrushType == null || GradientStopType == null || _colorParse == null) return null;
        try
        {
            var brush = Activator.CreateInstance(LinearGradientBrushType);
            if (brush == null) return null;


            if (RelativePointType != null)
            {
                object? startPoint = null, endPoint = null;


                var parseMethod = RelativePointType.GetMethod("Parse",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (parseMethod != null)
                {
                    try
                    {
                        string startStr = $"{startX * 100:F0}%,{startY * 100:F0}%";
                        string endStr = $"{endX * 100:F0}%,{endY * 100:F0}%";
                        startPoint = parseMethod.Invoke(null, new object[] { startStr });
                        endPoint = parseMethod.Invoke(null, new object[] { endStr });
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Reflection", $"RelativePoint.Parse failed: {ex.Message}");
                    }
                }


                if (startPoint == null)
                {
                    var relUnitType = RelativeUnitType ?? RelativePointType.GetConstructors()
                        .SelectMany(c => c.GetParameters())
                        .FirstOrDefault(p => p.ParameterType.Name == "RelativeUnit")?.ParameterType;

                    if (relUnitType != null)
                    {
                        try
                        {
                            var relativeUnit = Enum.Parse(relUnitType, "Relative");
                            startPoint = Activator.CreateInstance(RelativePointType, startX, startY, relativeUnit);
                            endPoint = Activator.CreateInstance(RelativePointType, endX, endY, relativeUnit);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("Reflection", $"RelativePoint ctor fallback failed: {ex.Message}");
                        }
                    }
                }

                if (startPoint != null)
                    brush.GetType().GetProperty("StartPoint")?.SetValue(brush, startPoint);
                if (endPoint != null)
                    brush.GetType().GetProperty("EndPoint")?.SetValue(brush, endPoint);

                Logger.Log("Reflection", $"LinearGradientBrush: start={startPoint != null} end={endPoint != null} method={(parseMethod != null ? "Parse" : "ctor")}");
            }


            var gradientStops = brush.GetType().GetProperty("GradientStops")?.GetValue(brush);
            if (gradientStops == null && GradientStopsType != null)
            {
                gradientStops = Activator.CreateInstance(GradientStopsType);
                brush.GetType().GetProperty("GradientStops")?.SetValue(brush, gradientStops);
            }

            if (gradientStops is IList stopList)
            {
                foreach (var (hex, offset) in stops)
                {
                    var color = _colorParse.Invoke(null, new object[] { hex });
                    if (color == null) continue;
                    var stop = Activator.CreateInstance(GradientStopType);
                    if (stop == null) continue;
                    stop.GetType().GetProperty("Color")?.SetValue(stop, color);
                    stop.GetType().GetProperty("Offset")?.SetValue(stop, offset);
                    stopList.Add(stop);
                }
            }

            return brush;
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"CreateLinearGradientBrush error: {ex.Message}");
            return null;
        }
    }

    public void SetBackgroundBrush(object? control, object? brush)
    {
        if (control == null) return;
        var bgProp = control.GetType().GetProperty("Background");
        bgProp?.SetValue(control, brush);
    }

    public void SubscribePointerEvent(object control, string eventName, Action<double, double> callback)
    {
        try
        {
            var type = control.GetType();
            var eventInfo = type.GetEvent(eventName);
            if (eventInfo == null)
            {
                Logger.Log("Reflection", $"PointerEvent '{eventName}' not found on {type.Name}");
                return;
            }

            var handlerType = eventInfo.EventHandlerType!;
            var invokeMethod = handlerType.GetMethod("Invoke")!;
            var paramTypes = invokeMethod.GetParameters().Select(p => p.ParameterType).ToArray();


            var p0 = Expression.Parameter(paramTypes[0], "sender");
            var p1 = Expression.Parameter(paramTypes[1], "e");


            var getPositionMethod = paramTypes[1].GetMethod("GetPosition",
                new[] { VisualType ?? typeof(object) });

            getPositionMethod ??= paramTypes[1].GetMethods()
                .FirstOrDefault(m => m.Name == "GetPosition" && m.GetParameters().Length == 1);

            if (getPositionMethod == null)
            {
                Logger.Log("Reflection", $"GetPosition not found on {paramTypes[1].Name}");
                return;
            }


            var castSender = Expression.Convert(p0, getPositionMethod.GetParameters()[0].ParameterType);
            var posExpr = Expression.Call(p1, getPositionMethod, castSender);

            var posXProp = getPositionMethod.ReturnType.GetProperty("X");
            var posYProp = getPositionMethod.ReturnType.GetProperty("Y");
            if (posXProp == null || posYProp == null) return;

            var xExpr = Expression.Property(posExpr, posXProp);
            var yExpr = Expression.Property(posExpr, posYProp);

            var callbackExpr = Expression.Constant(callback);
            var invokeExpr = Expression.Invoke(callbackExpr, xExpr, yExpr);
            var lambda = Expression.Lambda(handlerType, invokeExpr, p0, p1);
            var handler = lambda.Compile();

            eventInfo.AddEventHandler(control, handler);
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"SubscribePointerEvent({eventName}) failed: {ex.Message}");
        }
    }

    public void SetClipToBounds(object? control, bool clip)
    {
        if (control == null) return;
        control.GetType().GetProperty("ClipToBounds")?.SetValue(control, clip);
    }

    public void EnumerateResources(object? resources, Action<string, object?> callback)
    {
        if (resources == null) return;
        try
        {

            if (resources is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null) continue;
                    var itemType = item.GetType();
                    var keyProp = itemType.GetProperty("Key");
                    var valueProp = itemType.GetProperty("Value");
                    if (keyProp != null && valueProp != null)
                    {
                        var key = keyProp.GetValue(item)?.ToString() ?? "null";
                        var value = valueProp.GetValue(item);
                        callback(key, value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Reflection", $"EnumerateResources error: {ex.Message}");
        }
    }
}
