namespace Vakage;

/// <summary>
/// Tiny append-only log in %LOCALAPPDATA%\Vakage\vakage.log. Best-effort:
/// logging must never take the app down.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(AppPaths.UserDataDirectory, "vakage.log");
    private const long MaxBytes = 1024 * 1024;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.UserDataDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    var old = LogPath + ".old";
                    File.Delete(old);
                    File.Move(LogPath, old);
                }
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Best effort only.
        }
    }
}
