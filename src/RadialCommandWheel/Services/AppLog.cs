using System.Text;

namespace RadialCommandWheel.Services;

public static class AppLog
{
    private static readonly object SyncRoot = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RadialCommandWheel",
        "app.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var builder = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("O"))
                    .Append("  ")
                    .AppendLine(message);
                if (exception is not null)
                {
                    builder.AppendLine(exception.ToString());
                }

                File.AppendAllText(LogPath, builder.ToString());
            }
        }
        catch
        {
            // Logging must never prevent the application from running.
        }
    }
}

