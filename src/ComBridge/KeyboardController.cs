using System.Runtime.InteropServices;

namespace ComBridge;

public sealed record KeyboardStroke(ushort VirtualKey, ushort ScanCode, bool KeyUp, bool Unicode);

public static class InputFactory
{
    private static readonly IReadOnlyDictionary<string, ushort> NamedKeys = CreateNamedKeys();

    public static IReadOnlyList<KeyboardStroke> UnicodeText(string text)
    {
        if (text is null) throw new RequestValidationException("text is required.");
        var result = new List<KeyboardStroke>(text.Length * 2);
        foreach (var code_unit in text)
        {
            result.Add(new KeyboardStroke(0, code_unit, false, true));
            result.Add(new KeyboardStroke(0, code_unit, true, true));
        }
        return result;
    }

    public static IReadOnlyList<KeyboardStroke> Hotkey(IReadOnlyList<string> keys)
    {
        if (keys is null || keys.Count == 0) throw new RequestValidationException("keys must contain at least one key.");
        var virtual_keys = keys.Select(ParseKey).ToArray();
        return virtual_keys.Select(key => new KeyboardStroke(key, 0, false, false))
            .Concat(virtual_keys.Reverse().Select(key => new KeyboardStroke(key, 0, true, false))).ToArray();
    }

    private static ushort ParseKey(string key)
    {
        var normalized = key?.Trim().ToUpperInvariant() ?? "";
        if (NamedKeys.TryGetValue(normalized, out var value)) return value;
        if (normalized.Length == 1 && ((normalized[0] >= 'A' && normalized[0] <= 'Z') ||
                                     (normalized[0] >= '0' && normalized[0] <= '9'))) return normalized[0];
        throw new RequestValidationException($"Unknown key '{key}'.");
    }

    private static IReadOnlyDictionary<string, ushort> CreateNamedKeys()
    {
        var keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["CTRL"] = 0x11, ["ALT"] = 0x12, ["SHIFT"] = 0x10, ["WIN"] = 0x5B,
            ["ENTER"] = 0x0D, ["ESC"] = 0x1B, ["TAB"] = 0x09, ["BACKSPACE"] = 0x08,
            ["DELETE"] = 0x2E, ["SPACE"] = 0x20, ["UP"] = 0x26, ["DOWN"] = 0x28,
            ["LEFT"] = 0x25, ["RIGHT"] = 0x27, ["HOME"] = 0x24, ["END"] = 0x23,
            ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22
        };
        for (var number = 1; number <= 12; number++) keys[$"F{number}"] = (ushort)(0x6F + number);
        return keys;
    }
}

public interface IKeyboardController
{
    void Type(string text);
    void Hotkey(IReadOnlyList<string> keys);
}

public sealed class KeyboardController : IKeyboardController
{
    public void Type(string text) => Send(InputFactory.UnicodeText(text));
    public void Hotkey(IReadOnlyList<string> keys)
    {
        var strokes = InputFactory.Hotkey(keys);
        var half = strokes.Count / 2;
        try
        {
            Send(strokes.Take(half).ToArray());
        }
        finally
        {
            Send(strokes.Skip(half).ToArray());
        }
    }

    private static void Send(IReadOnlyList<KeyboardStroke> strokes)
    {
        DesktopService.EnsureWindows();
        var inputs = strokes.Select(stroke => new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            U = new NativeMethods.InputUnion { ki = new NativeMethods.KEYBDINPUT
            {
                wVk = stroke.VirtualKey, wScan = stroke.ScanCode,
                dwFlags = (stroke.KeyUp ? NativeMethods.KEYEVENTF_KEYUP : 0) |
                          (stroke.Unicode ? NativeMethods.KEYEVENTF_UNICODE : 0)
            }}
        }).ToArray();
        if (inputs.Length == 0) return;
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length) throw new WindowsApiException("SendInput(keyboard)", Marshal.GetLastWin32Error());
    }
}
