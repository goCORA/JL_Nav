using System.IO;

namespace JL_Nav;

internal static class Diagnostics
{
    public static string LogPath { get; } = Path.Combine(Path.GetTempPath(), "JL_Nav_crash.log");

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

    /// <summary>Opens the log file in the user's default viewer, creating it first if nothing has been logged yet.</summary>
    public static void OpenLog()
    {
        try
        {
            if (!File.Exists(LogPath))
                File.WriteAllText(LogPath, string.Empty);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch { /* best effort */ }
    }
}
