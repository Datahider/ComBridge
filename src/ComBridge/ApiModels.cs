namespace ComBridge;

public sealed record MoveRequest(int X, int Y);
public sealed record ClickRequest(int X, int Y, string Button = "left", int Count = 1);
public sealed record ScrollRequest(int Delta, int? X = null, int? Y = null);
public sealed record DragRequest(int FromX, int FromY, int ToX, int ToY, int DurationMs = 500);
public sealed record TypeRequest(string Text);
public sealed record HotkeyRequest(IReadOnlyList<string> Keys);
public sealed record ApiError(string Error, string Message);
public sealed record CommandResult(bool Success, string Action);

public static class ApiErrors
{
    public static ApiError InvalidRequest(string message) => new("invalid_request", message);
    public static ApiError DesktopUnavailable(string message) => new("desktop_unavailable", message);
    public static ApiError WindowsApi(string message) => new("windows_api_error", message);
}

public sealed class RequestValidationException(string message) : Exception(message);
public sealed class DesktopUnavailableException(string message) : Exception(message);
public sealed class WindowsApiException(string operation, int error_code)
    : Exception($"{operation} failed with Win32 error {error_code}.")
{
    public int ErrorCode { get; } = error_code;
}

public static class RequestValidator
{
    public static void Validate(ClickRequest request)
    {
        if (!new[] { "left", "right", "middle" }.Contains(request.Button, StringComparer.OrdinalIgnoreCase))
            throw new RequestValidationException("button must be left, right, or middle.");
        if (request.Count is < 1 or > 2)
            throw new RequestValidationException("count must be 1 or 2.");
    }

    public static void Validate(ScrollRequest request)
    {
        if (request.Delta == 0) throw new RequestValidationException("delta must not be zero.");
        if (request.X.HasValue != request.Y.HasValue)
            throw new RequestValidationException("x and y must be supplied together.");
    }

    public static void Validate(DragRequest request)
    {
        if (request.DurationMs is < 0 or > 30000)
            throw new RequestValidationException("durationMs must be between 0 and 30000.");
    }

    public static void Validate(TypeRequest request)
    {
        if (request.Text is null) throw new RequestValidationException("text is required.");
        if (request.Text.Length > 100000) throw new RequestValidationException("text is too long.");
    }

    public static void Validate(HotkeyRequest request)
    {
        if (request.Keys is null || request.Keys.Count == 0)
            throw new RequestValidationException("keys must contain at least one key.");
        if (request.Keys.Count > 32) throw new RequestValidationException("too many keys.");
    }
}

