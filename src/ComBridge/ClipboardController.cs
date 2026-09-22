using System.Runtime.InteropServices;

namespace ComBridge;

public interface IClipboardController
{
    string GetText();
    void SetText(string text);
}

public sealed class ClipboardController : IClipboardController
{
    public string GetText()
    {
        DesktopService.EnsureWindows();
        Open();
        var opened = true;
        try
        {
            if (!NativeMethods.IsClipboardFormatAvailable(NativeMethods.CF_UNICODETEXT))
                throw new ClipboardTextUnavailableException("The clipboard does not contain CF_UNICODETEXT.");
            var memory = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
            if (memory == nint.Zero) throw LastError("GetClipboardData");
            var bytes = NativeMethods.GlobalSize(memory);
            if (bytes == 0) throw LastError("GlobalSize");
            var pointer = NativeMethods.GlobalLock(memory);
            if (pointer == nint.Zero) throw LastError("GlobalLock");
            string text;
            try
            {
                var char_count = checked((int)(bytes / 2));
                text = Marshal.PtrToStringUni(pointer, char_count) ?? "";
                var terminator = text.IndexOf('\0');
                if (terminator >= 0) text = text[..terminator];
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }
            Close();
            opened = false;
            return text;
        }
        finally
        {
            if (opened) NativeMethods.CloseClipboard();
        }
    }

    public void SetText(string text)
    {
        DesktopService.EnsureWindows();
        var byte_count = checked((nuint)((text.Length + 1L) * 2L));
        var memory = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, byte_count);
        if (memory == nint.Zero) throw LastError("GlobalAlloc");
        var owns_memory = true;
        try
        {
            var pointer = NativeMethods.GlobalLock(memory);
            if (pointer == nint.Zero) throw LastError("GlobalLock");
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, checked(text.Length * 2), 0);
            }
            finally
            {
                NativeMethods.GlobalUnlock(memory);
            }

            Open();
            var opened = true;
            try
            {
                if (!NativeMethods.EmptyClipboard()) throw LastError("EmptyClipboard");
                if (NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) == nint.Zero)
                    throw LastError("SetClipboardData");
                owns_memory = false;
                Close();
                opened = false;
            }
            finally
            {
                if (opened) NativeMethods.CloseClipboard();
            }
        }
        finally
        {
            if (owns_memory) NativeMethods.GlobalFree(memory);
        }
    }

    private static void Open()
    {
        if (!NativeMethods.OpenClipboard(nint.Zero)) throw LastError("OpenClipboard");
    }

    private static void Close()
    {
        if (!NativeMethods.CloseClipboard()) throw LastError("CloseClipboard");
    }

    private static WindowsApiException LastError(string operation) => new(operation, Marshal.GetLastWin32Error());
}
