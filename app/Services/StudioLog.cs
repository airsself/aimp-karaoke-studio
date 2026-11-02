using System.IO;
namespace KaraokeStudio.Services;
internal static class StudioLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AimpKaraokeStudio", "karaoke.log");
    public static void Error(string context, Exception error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000)
                    File.Move(LogPath, LogPath + ".previous", true);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {context}: {error}\n");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
