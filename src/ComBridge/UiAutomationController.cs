using System.Runtime.InteropServices;
using Interop.UIAutomationClient;

namespace ComBridge;

public sealed record UiElementSnapshot(string Name, string AutomationId, string ControlType, string ClassName);
public sealed record UiSelector(string? Name = null, string? AutomationId = null,
    string? ControlType = null, string? ClassName = null)
{
    public bool Matches(UiElementSnapshot element) =>
        MatchesField(Name, element.Name) && MatchesField(AutomationId, element.AutomationId) &&
        MatchesField(ControlType, element.ControlType) && MatchesField(ClassName, element.ClassName);

    private static bool MatchesField(string? expected, string actual) =>
        expected is null || string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
}

public sealed record UiFindRequest(string WindowId, UiSelector Selector, int MaxResults = 50);
public sealed record UiActionRequest(string WindowId, UiSelector Selector, string Action, string? Value = null);
public sealed record UiTextRequest(string WindowId, UiSelector Selector, int MaxLength = 1_000_000);
public sealed record UiTextResponse(string Text, int Length, string Source);
public sealed record UiActionResponse(bool Success, string Action, UiElementInfo Element);
public sealed record UiTreeResponse(string WindowId, int Count, bool Truncated, IReadOnlyList<UiElementInfo> Elements);
public sealed record UiFindResponse(int Count, bool Truncated, IReadOnlyList<UiElementInfo> Elements);
public sealed record UiElementInfo(int Index, int? ParentIndex, int Depth, string Name, string AutomationId,
    string ControlType, string ClassName, bool IsEnabled, bool IsOffscreen, RectangleInfo DesktopBounds,
    RectangleInfo ImageBounds, IReadOnlyList<string> SupportedActions);

public sealed class UiElementNotFoundException(string message) : Exception(message);
public sealed class UiElementAmbiguousException(string message) : Exception(message);
public sealed class UiPatternUnsupportedException(string message) : Exception(message);
public sealed class UiAutomationOperationException(string message, Exception? inner = null) : Exception(message, inner);

public static class UiRequestValidator
{
    private static readonly string[] Actions = ["focus", "invoke", "select", "expand", "collapse", "setvalue"];

    public static void Validate(UiSelector selector)
    {
        if (selector is null || new[] { selector.Name, selector.AutomationId, selector.ControlType, selector.ClassName }
                .All(string.IsNullOrWhiteSpace))
            throw new RequestValidationException("At least one UI selector field is required.");
    }

    public static void ValidateTree(int depth, int max_nodes)
    {
        if (depth is < 0 or > 10) throw new RequestValidationException("depth must be between 0 and 10.");
        if (max_nodes is < 1 or > 2000) throw new RequestValidationException("maxNodes must be between 1 and 2000.");
    }

    public static void Validate(UiFindRequest request)
    {
        _ = WindowId.Parse(request.WindowId);
        Validate(request.Selector);
        if (request.MaxResults is < 1 or > 200)
            throw new RequestValidationException("maxResults must be between 1 and 200.");
    }

    public static void Validate(UiActionRequest request)
    {
        _ = WindowId.Parse(request.WindowId);
        Validate(request.Selector);
        var action = request.Action?.Trim().ToLowerInvariant() ?? "";
        if (!Actions.Contains(action)) throw new RequestValidationException("Unsupported UI action.");
        if (action == "setvalue" && request.Value is null)
            throw new RequestValidationException("value is required for setValue.");
    }

    public static void Validate(UiTextRequest request)
    {
        _ = WindowId.Parse(request.WindowId);
        Validate(request.Selector);
        if (request.MaxLength is < 1 or > 10_000_000)
            throw new RequestValidationException("maxLength must be between 1 and 10000000.");
    }
}

public interface IUiAutomationController
{
    UiTreeResponse Tree(string window_id, int depth, int max_nodes, VirtualScreen screen);
    UiFindResponse Find(UiFindRequest request, VirtualScreen screen);
    UiActionResponse Act(UiActionRequest request, VirtualScreen screen);
    UiTextResponse Text(UiTextRequest request);
}

public sealed class UiAutomationController : IUiAutomationController
{
    public UiTreeResponse Tree(string window_id, int depth, int max_nodes, VirtualScreen screen)
    {
        DesktopService.EnsureWindows();
        UiRequestValidator.ValidateTree(depth, max_nodes);
        var window = WindowId.Parse(window_id);
        EnsureWindow(window);
        try
        {
            IUIAutomation automation = new CUIAutomation8();
            var result = Traverse(automation.ElementFromHandle(window), automation.ControlViewWalker, depth, max_nodes, screen);
            return new UiTreeResponse(WindowId.Format(window), result.Elements.Count, result.Truncated, result.Elements);
        }
        catch (Exception exception) when (exception is not RequestValidationException)
        {
            throw new UiAutomationOperationException("UI Automation tree enumeration failed.", exception);
        }
    }

    public UiFindResponse Find(UiFindRequest request, VirtualScreen screen)
    {
        DesktopService.EnsureWindows();
        UiRequestValidator.Validate(request);
        var window = WindowId.Parse(request.WindowId);
        EnsureWindow(window);
        try
        {
            IUIAutomation automation = new CUIAutomation8();
            var matches = FindMatches(automation.ElementFromHandle(window), automation.ControlViewWalker, request.Selector, request.MaxResults + 1);
            var truncated = matches.Count > request.MaxResults;
            var elements = matches.Take(request.MaxResults).Select((element, index) => Describe(element, index, null, 0, screen)).ToArray();
            return new UiFindResponse(elements.Length, truncated, elements);
        }
        catch (Exception exception) when (exception is not RequestValidationException)
        {
            throw new UiAutomationOperationException("UI Automation search failed.", exception);
        }
    }

    public UiActionResponse Act(UiActionRequest request, VirtualScreen screen)
    {
        DesktopService.EnsureWindows();
        UiRequestValidator.Validate(request);
        var window = WindowId.Parse(request.WindowId);
        EnsureWindow(window);
        try
        {
            IUIAutomation automation = new CUIAutomation8();
            var matches = FindMatches(automation.ElementFromHandle(window), automation.ControlViewWalker, request.Selector, 2);
            var element = RequireOne(matches, request.Selector);
            var action = request.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "focus": element.SetFocus(); break;
                case "invoke": RequirePattern<IUIAutomationInvokePattern>(element, UIA_PatternIds.UIA_InvokePatternId, "Invoke").Invoke(); break;
                case "select": RequirePattern<IUIAutomationSelectionItemPattern>(element, UIA_PatternIds.UIA_SelectionItemPatternId, "SelectionItem").Select(); break;
                case "expand": RequirePattern<IUIAutomationExpandCollapsePattern>(element, UIA_PatternIds.UIA_ExpandCollapsePatternId, "ExpandCollapse").Expand(); break;
                case "collapse": RequirePattern<IUIAutomationExpandCollapsePattern>(element, UIA_PatternIds.UIA_ExpandCollapsePatternId, "ExpandCollapse").Collapse(); break;
                case "setvalue": RequirePattern<IUIAutomationValuePattern>(element, UIA_PatternIds.UIA_ValuePatternId, "Value").SetValue(request.Value!); break;
            }
            return new UiActionResponse(true, action, Describe(element, 0, null, 0, screen));
        }
        catch (Exception exception) when (exception is not (RequestValidationException or UiElementNotFoundException or UiElementAmbiguousException or UiPatternUnsupportedException))
        {
            throw new UiAutomationOperationException("UI Automation action failed.", exception);
        }
    }

    public UiTextResponse Text(UiTextRequest request)
    {
        DesktopService.EnsureWindows();
        UiRequestValidator.Validate(request);
        var window = WindowId.Parse(request.WindowId);
        EnsureWindow(window);
        try
        {
            IUIAutomation automation = new CUIAutomation8();
            var element = RequireOne(FindMatches(automation.ElementFromHandle(window), automation.ControlViewWalker, request.Selector, 2), request.Selector);
            var text_pattern = GetPattern<IUIAutomationTextPattern>(element, UIA_PatternIds.UIA_TextPatternId);
            if (text_pattern is not null)
            {
                var text = text_pattern.DocumentRange.GetText(request.MaxLength);
                return new UiTextResponse(text, text.Length, "TextPattern");
            }
            var value_pattern = GetPattern<IUIAutomationValuePattern>(element, UIA_PatternIds.UIA_ValuePatternId);
            if (value_pattern is not null)
            {
                var text = value_pattern.CurrentValue ?? "";
                if (text.Length > request.MaxLength) text = text[..request.MaxLength];
                return new UiTextResponse(text, text.Length, "ValuePattern");
            }
            throw new UiPatternUnsupportedException("The matched element supports neither TextPattern nor ValuePattern.");
        }
        catch (Exception exception) when (exception is not (RequestValidationException or UiElementNotFoundException or UiElementAmbiguousException or UiPatternUnsupportedException))
        {
            throw new UiAutomationOperationException("UI Automation text retrieval failed.", exception);
        }
    }

    private static (List<UiElementInfo> Elements, bool Truncated) Traverse(IUIAutomationElement root, IUIAutomationTreeWalker walker, int max_depth,
        int max_nodes, VirtualScreen screen)
    {
        var result = new List<UiElementInfo>();
        var queue = new Queue<(IUIAutomationElement Element, int? Parent, int Depth)>();
        queue.Enqueue((root, null, 0));
        var truncated = false;
        while (queue.Count > 0)
        {
            if (result.Count >= max_nodes) { truncated = true; break; }
            var current = queue.Dequeue();
            var index = result.Count;
            result.Add(Describe(current.Element, index, current.Parent, current.Depth, screen));
            if (current.Depth >= max_depth) continue;
            foreach (var child in Children(walker, current.Element)) queue.Enqueue((child, index, current.Depth + 1));
        }
        return (result, truncated);
    }

    private static List<IUIAutomationElement> FindMatches(IUIAutomationElement root, IUIAutomationTreeWalker walker, UiSelector selector, int limit)
    {
        var result = new List<IUIAutomationElement>();
        var queue = new Queue<IUIAutomationElement>();
        queue.Enqueue(root);
        while (queue.Count > 0 && result.Count < limit)
        {
            var element = queue.Dequeue();
            if (selector.Matches(Snapshot(element))) result.Add(element);
            foreach (var child in Children(walker, element)) queue.Enqueue(child);
        }
        return result;
    }

    private static IUIAutomationElement RequireOne(IReadOnlyList<IUIAutomationElement> matches, UiSelector selector)
    {
        if (matches.Count == 0) throw new UiElementNotFoundException($"No UI element matches {selector}.");
        if (matches.Count > 1) throw new UiElementAmbiguousException($"More than one UI element matches {selector}.");
        return matches[0];
    }

    private static UiElementSnapshot Snapshot(IUIAutomationElement element) =>
        new(element.CurrentName ?? "", element.CurrentAutomationId ?? "", ControlTypeName(element.CurrentControlType), element.CurrentClassName ?? "");

    private static UiElementInfo Describe(IUIAutomationElement element, int index, int? parent, int depth, VirtualScreen screen)
    {
        var snapshot = Snapshot(element);
        var rectangle = element.CurrentBoundingRectangle;
        var desktop_bounds = new RectangleInfo(rectangle.left, rectangle.top, rectangle.right - rectangle.left, rectangle.bottom - rectangle.top);
        var actions = new List<string> { "focus" };
        if (GetPattern<IUIAutomationInvokePattern>(element, UIA_PatternIds.UIA_InvokePatternId) is not null) actions.Add("invoke");
        if (GetPattern<IUIAutomationSelectionItemPattern>(element, UIA_PatternIds.UIA_SelectionItemPatternId) is not null) actions.Add("select");
        if (GetPattern<IUIAutomationExpandCollapsePattern>(element, UIA_PatternIds.UIA_ExpandCollapsePatternId) is not null) { actions.Add("expand"); actions.Add("collapse"); }
        var has_value = GetPattern<IUIAutomationValuePattern>(element, UIA_PatternIds.UIA_ValuePatternId) is not null;
        if (has_value) actions.Add("setValue");
        if (has_value || GetPattern<IUIAutomationTextPattern>(element, UIA_PatternIds.UIA_TextPatternId) is not null) actions.Add("text");
        return new UiElementInfo(index, parent, depth, snapshot.Name, snapshot.AutomationId, snapshot.ControlType,
            snapshot.ClassName, element.CurrentIsEnabled != 0, element.CurrentIsOffscreen != 0, desktop_bounds,
            WindowCoordinateMapper.ToImageBounds(desktop_bounds, screen), actions);
    }

    private static T RequirePattern<T>(IUIAutomationElement element, int pattern_id, string pattern) where T : class
    {
        return GetPattern<T>(element, pattern_id) ??
            throw new UiPatternUnsupportedException($"The matched element does not support {pattern}Pattern.");
    }

    private static T? GetPattern<T>(IUIAutomationElement element, int pattern_id) where T : class
    {
        try { return element.GetCurrentPattern(pattern_id) as T; }
        catch (COMException) { return null; }
    }

    private static IEnumerable<IUIAutomationElement> Children(IUIAutomationTreeWalker walker, IUIAutomationElement parent)
    {
        var child = walker.GetFirstChildElement(parent);
        while (child is not null)
        {
            yield return child;
            child = walker.GetNextSiblingElement(child);
        }
    }

    private static string ControlTypeName(int id) => id switch
    {
        50000 => "Button", 50001 => "Calendar", 50002 => "CheckBox", 50003 => "ComboBox",
        50004 => "Edit", 50005 => "Hyperlink", 50006 => "Image", 50007 => "ListItem",
        50008 => "List", 50009 => "Menu", 50010 => "MenuBar", 50011 => "MenuItem",
        50012 => "ProgressBar", 50013 => "RadioButton", 50014 => "ScrollBar", 50015 => "Slider",
        50016 => "Spinner", 50017 => "StatusBar", 50018 => "Tab", 50019 => "TabItem",
        50020 => "Text", 50021 => "ToolBar", 50022 => "ToolTip", 50023 => "Tree",
        50024 => "TreeItem", 50025 => "Custom", 50026 => "Group", 50027 => "Thumb",
        50028 => "DataGrid", 50029 => "DataItem", 50030 => "Document", 50031 => "SplitButton",
        50032 => "Window", 50033 => "Pane", 50034 => "Header", 50035 => "HeaderItem",
        50036 => "Table", 50037 => "TitleBar", 50038 => "Separator", 50039 => "SemanticZoom",
        50040 => "AppBar", _ => $"ControlType{id}"
    };

    private static void EnsureWindow(nint window)
    {
        if (!NativeMethods.IsWindow(window)) throw new RequestValidationException("The window id no longer exists.");
    }
}
