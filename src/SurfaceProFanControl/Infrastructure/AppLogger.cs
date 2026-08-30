using System;
using System.IO;
using System.Text;

namespace SurfaceProFanControl.Infrastructure
{
    public static class AppLogger
    {
        private static readonly object Gate = new object();

        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SurfaceProFanControl",
            "surface-pro-fan-control.log");

        public static void Info(string message) => Write("INFO", message, null);

        public static void Error(string message, Exception exception) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception? exception)
        {
            try
            {
                lock (Gate)
                {
                    var directory = Path.GetDirectoryName(LogPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                    if (exception != null)
                    {
                        line += Environment.NewLine + exception;
                    }

                    File.AppendAllText(LogPath, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never interrupt thermal control or rollback.
            }
        }
    }
}
