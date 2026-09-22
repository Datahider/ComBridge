using System.Reflection;
using System.Text.Json;
using ComBridge;

var options = CommandLineOptions.Parse(args);
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://{options.Address}:{options.Port}");
builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
builder.Services.AddSingleton<FileLog>();
builder.Services.AddSingleton<IDesktopService, DesktopService>();
builder.Services.AddSingleton<IScreenCapture, ScreenCapture>();
builder.Services.AddSingleton<IMouseController, MouseController>();
builder.Services.AddSingleton<IKeyboardController, KeyboardController>();
builder.Services.AddSingleton<IClipboardController, ClipboardController>();
builder.Services.AddSingleton<IWindowController, WindowController>();
builder.Services.AddSingleton<IUiAutomationController, UiAutomationController>();
builder.Services.AddSingleton<UiOperationGate>();

var app = builder.Build();
var log = app.Services.GetRequiredService<FileLog>();
log.Write("INFO", $"Starting ComBridge on http://{options.Address}:{options.Port}");
app.Lifetime.ApplicationStopped.Register(() => { log.Write("INFO", "ComBridge stopped"); log.Dispose(); });

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException exception)
    {
        log.Write("WARN", $"Invalid HTTP request: {exception.Message}");
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(ApiErrors.InvalidRequest(exception.Message));
    }
    catch (RequestValidationException exception)
    {
        log.Write("WARN", $"Validation failed: {exception.Message}");
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(ApiErrors.InvalidRequest(exception.Message));
    }
    catch (DesktopUnavailableException exception)
    {
        log.Write("ERROR", exception.Message);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(ApiErrors.DesktopUnavailable(exception.Message));
    }
    catch (ClipboardTextUnavailableException exception)
    {
        log.Write("WARN", exception.Message);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(ApiErrors.ClipboardTextUnavailable(exception.Message));
    }
    catch (WindowActivationException exception)
    {
        log.Write("WARN", exception.Message);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(ApiErrors.WindowActivationFailed(exception.Message));
    }
    catch (UiElementNotFoundException exception)
    {
        log.Write("WARN", exception.Message);
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(ApiErrors.UiElementNotFound(exception.Message));
    }
    catch (UiElementAmbiguousException exception)
    {
        log.Write("WARN", exception.Message);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(ApiErrors.UiElementAmbiguous(exception.Message));
    }
    catch (UiPatternUnsupportedException exception)
    {
        log.Write("WARN", exception.Message);
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(ApiErrors.UiPatternUnsupported(exception.Message));
    }
    catch (UiAutomationOperationException exception)
    {
        log.Write("ERROR", $"{exception.Message} {exception.InnerException?.Message}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(ApiErrors.UiAutomationFailed(exception.Message));
    }
    catch (Exception exception) when (exception is WindowsApiException or PlatformNotSupportedException)
    {
        log.Write("ERROR", exception.Message);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(ApiErrors.WindowsApi(exception.Message));
    }
});

app.MapGet("/health", (IDesktopService desktop) =>
{
    var available = desktop.IsDesktopAvailable(out var diagnostic);
    var width = 0; var height = 0;
    if (available)
    {
        var screen = desktop.GetScreenInfo().VirtualScreen;
        width = screen.Width; height = screen.Height;
    }
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    return new HealthResponse(available ? "ok" : "degraded", version, desktop.GetSessionId(), available, width, height, diagnostic);
});

app.MapGet("/screen/info", async (IDesktopService desktop, UiOperationGate gate, CancellationToken token) =>
    await gate.RunAsync(() => Task.FromResult(desktop.RequireScreenInfo()), token));

app.MapGet("/screen", async (int? monitor, int? x, int? y, int? width, int? height, string? format, int? quality,
    IDesktopService desktop, IScreenCapture capture, UiOperationGate gate, CancellationToken token) =>
{
    var result = await gate.RunAsync(() =>
    {
        var info = desktop.RequireScreenInfo();
        var request = new ScreenRequest(monitor, x, y, width, height, format ?? "png", quality ?? 80);
        var area = ScreenRequestResolver.Resolve(info, request);
        var output = ScreenRequestResolver.ResolveFormat(request.Format, request.Quality);
        return Task.FromResult((Bytes: capture.Capture(info, area, output), Output: output));
    }, token);
    return Results.File(result.Bytes, result.Output.ContentType);
});

app.MapGet("/windows", (IDesktopService desktop, IWindowController windows, UiOperationGate gate, CancellationToken token) =>
    gate.RunAsync(() => Task.FromResult(windows.List(desktop.RequireScreenInfo().VirtualScreen)), token));
app.MapPost("/windows/activate", (ActivateWindowRequest request, IWindowController windows, UiOperationGate gate, CancellationToken token) =>
    gate.RunAsync(() => Task.FromResult(windows.Activate(request.Id)), token));
app.MapGet("/mouse/position", (IDesktopService desktop, IMouseController mouse, UiOperationGate gate, CancellationToken token) =>
    gate.RunAsync(() => Task.FromResult(mouse.GetPosition(desktop.RequireScreenInfo())), token));
app.MapGet("/ui/tree", (string windowId, int? depth, int? maxNodes, IDesktopService desktop,
    IUiAutomationController ui, UiOperationGate gate, CancellationToken token) => gate.RunAsync(() =>
        Task.FromResult(ui.Tree(windowId, depth ?? 3, maxNodes ?? 500, desktop.RequireScreenInfo().VirtualScreen)), token));
app.MapPost("/ui/find", (UiFindRequest request, IDesktopService desktop, IUiAutomationController ui,
    UiOperationGate gate, CancellationToken token) => gate.RunAsync(() =>
        Task.FromResult(ui.Find(request, desktop.RequireScreenInfo().VirtualScreen)), token));
app.MapPost("/ui/action", (UiActionRequest request, IDesktopService desktop, IUiAutomationController ui,
    UiOperationGate gate, CancellationToken token) => gate.RunAsync(() =>
        Task.FromResult(ui.Act(request, desktop.RequireScreenInfo().VirtualScreen)), token));
app.MapPost("/ui/text", (UiTextRequest request, IUiAutomationController ui, UiOperationGate gate,
    CancellationToken token) => gate.RunAsync(() => Task.FromResult(ui.Text(request)), token));

app.MapPost("/mouse/move", (MoveRequest request, IDesktopService desktop, IMouseController mouse, UiOperationGate gate, CancellationToken token) =>
    Command(gate, token, log, "mouse/move", () => mouse.Move(request.X, request.Y, desktop.RequireScreenInfo())));
app.MapPost("/mouse/click", (ClickRequest request, IDesktopService desktop, IMouseController mouse, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    return Command(gate, token, log, "mouse/click", () => mouse.Click(request, desktop.RequireScreenInfo()));
});
app.MapPost("/mouse/scroll", (ScrollRequest request, IDesktopService desktop, IMouseController mouse, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    return Command(gate, token, log, "mouse/scroll", () => mouse.Scroll(request, desktop.RequireScreenInfo()));
});
app.MapPost("/mouse/drag", (DragRequest request, IDesktopService desktop, IMouseController mouse, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    return gate.RunAsync(async () => { log.Write("INFO", "Command mouse/drag"); await mouse.DragAsync(request, desktop.RequireScreenInfo(), token); log.Write("INFO", "Completed mouse/drag"); return new CommandResult(true, "mouse/drag"); }, token);
});
app.MapPost("/keyboard/type", (TypeRequest request, IKeyboardController keyboard, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    log.Write("INFO", $"Command keyboard/type length={request.Text.Length}");
    return gate.RunAsync(() => { keyboard.Type(request.Text); log.Write("INFO", "Completed keyboard/type"); return Task.FromResult(new CommandResult(true, "keyboard/type")); }, token);
});
app.MapPost("/keyboard/hotkey", (HotkeyRequest request, IKeyboardController keyboard, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    _ = InputFactory.Hotkey(request.Keys);
    return Command(gate, token, log, "keyboard/hotkey", () => keyboard.Hotkey(request.Keys));
});
app.MapGet("/clipboard/text", (IClipboardController clipboard, UiOperationGate gate, CancellationToken token) =>
    gate.RunAsync(() =>
    {
        var text = clipboard.GetText();
        log.Write("INFO", $"Command clipboard/get length={text.Length}");
        return Task.FromResult(ClipboardTextResponse.Create(text));
    }, token));
app.MapPost("/clipboard/text", (ClipboardTextRequest request, IClipboardController clipboard, UiOperationGate gate, CancellationToken token) =>
{
    RequestValidator.Validate(request);
    return gate.RunAsync(() =>
    {
        log.Write("INFO", $"Command clipboard/set length={request.Text.Length}");
        clipboard.SetText(request.Text);
        log.Write("INFO", "Completed clipboard/set");
        return Task.FromResult(new CommandResult(true, "clipboard/set"));
    }, token);
});

await app.RunAsync();

static Task<CommandResult> Command(UiOperationGate gate, CancellationToken token, FileLog log, string action, Action operation) =>
    gate.RunAsync(() => { log.Write("INFO", $"Command {action}"); operation(); log.Write("INFO", $"Completed {action}"); return Task.FromResult(new CommandResult(true, action)); }, token);

public sealed class UiOperationGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);
    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellation_token)
    {
        await semaphore.WaitAsync(cancellation_token);
        try { return await operation(); }
        finally { semaphore.Release(); }
    }
}

public sealed record CommandLineOptions(string Address, int Port)
{
    public static CommandLineOptions Parse(string[] args)
    {
        var address = "127.0.0.1"; var port = 8088;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--port")
            {
                if (++index >= args.Length || !int.TryParse(args[index], out port))
                    throw new ArgumentException("--port requires an integer value.");
            }
            else if (args[index] == "--address")
            {
                if (++index >= args.Length) throw new ArgumentException("--address requires a value.");
                address = args[index];
            }
        }
        if (port is < 1 or > 65535) throw new ArgumentException("--port must be between 1 and 65535.");
        if (!System.Net.IPAddress.TryParse(address, out var ip) || !System.Net.IPAddress.IsLoopback(ip))
            throw new ArgumentException("--address must be a loopback IP address.");
        return new CommandLineOptions(address, port);
    }
}
