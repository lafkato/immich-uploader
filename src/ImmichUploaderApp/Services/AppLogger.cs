namespace ImmichUploaderApp.Services;

public static class AppLogger
{
    private static readonly object Lock = new();
    private static string LogPath => Path.Combine(ConfigService.LogDir, "app.log");
    private const long MaxSizeBytes = 5 * 1024 * 1024;

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(ConfigService.LogDir);
                RollIfTooBig();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Lokitus ei saa koskaan kaataa sovellusta.
        }
    }

    public static void LogFatal(Exception? ex)
    {
        Log($"FATAL: {ex}");
    }

    private static void RollIfTooBig()
    {
        var info = new FileInfo(LogPath);
        if (!info.Exists || info.Length < MaxSizeBytes) return;

        var rolledPath = LogPath + ".old";
        File.Delete(rolledPath);
        File.Move(LogPath, rolledPath);
    }
}
