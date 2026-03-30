namespace TinyClips.Services;

internal static class AppLog
{
    private const long MaxLogSize = 1024 * 1024; // 1 MB

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TinyClips");

    private static readonly string LogPath = Path.Combine(LogDir, "tinyclips.log");

    private static readonly object _lock = new();

    internal static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.Message}");

    internal static void Info(string message) => Write("INFO", message);

    internal static string GetLogPath() => LogPath;

    internal static string ReadLog()
    {
        try
        {
            return File.Exists(LogPath) ? File.ReadAllText(LogPath) : "(no log file)";
        }
        catch
        {
            return "(could not read log)";
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            RotateIfNeeded();
            lock (_lock)
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { } // Logger must never throw
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(LogPath)) return;
            var info = new FileInfo(LogPath);
            if (info.Length <= MaxLogSize) return;

            // Keep the last half of the file
            var lines = File.ReadAllLines(LogPath);
            var half = lines.Length / 2;
            lock (_lock)
            {
                File.WriteAllLines(LogPath, lines[half..]);
            }
        }
        catch { }
    }
}
