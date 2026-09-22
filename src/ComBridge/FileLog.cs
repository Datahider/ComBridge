namespace ComBridge;

public sealed class FileLog : IDisposable
{
    private readonly object sync = new();
    private readonly StreamWriter writer;

    public FileLog()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) root = AppContext.BaseDirectory;
        var directory = Path.Combine(root, "ComBridge", "logs");
        Directory.CreateDirectory(directory);
        writer = new StreamWriter(Path.Combine(directory, $"combridge-{DateTime.Now:yyyyMMdd}.log"), append: true) { AutoFlush = true };
    }

    public void Write(string level, string message)
    {
        lock (sync) writer.WriteLine($"{DateTimeOffset.Now:O} [{level}] {message}");
    }

    public void Dispose() => writer.Dispose();
}

