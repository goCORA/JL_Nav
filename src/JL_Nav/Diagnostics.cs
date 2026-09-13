using System.IO;

namespace JL_Nav;

internal static class Diagnostics
{
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "JL_Nav_crash.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch { /* best effort */ }
    }

    public static void LogException(string source, Exception? ex)
    {
        Log($"{source}:{Environment.NewLine}{ex}");
    }
}
