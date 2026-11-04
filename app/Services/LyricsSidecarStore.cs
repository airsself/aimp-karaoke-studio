using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KaraokeStudio.Services;

/// <summary>
/// Save the chosen lyrics to a separate UTF-8 .lrc sidecar. In particular,
/// do NOT call AIMP TagEditor.SetToAll/EditTag or touch audio metadata:
/// the user-provided Legacy Bridge RC1 had a known metadata-loss defect.
/// </summary>
public static class LyricsSidecarStore
{
    public static bool TrySuggestPath(string? audioFile, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(audioFile) ||
            audioFile.StartsWith(@"\\", StringComparison.Ordinal) ||
            !Path.IsPathFullyQualified(audioFile) ||
            !File.Exists(audioFile)) return false;
        try
        {
            path = Path.ChangeExtension(audioFile, ".lrc");
            return !string.Equals(path, audioFile, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    public static async Task SaveAsync(string target, string lyrics,
        bool allowOverwrite, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(lyrics) || lyrics.Length > 150_000)
            throw new ArgumentException("Текст пустой или слишком большой.", nameof(lyrics));
        if (string.IsNullOrWhiteSpace(target) || !Path.IsPathFullyQualified(target) ||
            !string.Equals(Path.GetExtension(target), ".lrc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Для сохранения требуется абсолютный путь к .lrc.", nameof(target));
        if (File.Exists(target) && !allowOverwrite)
            throw new IOException("Файл LRC уже существует. Выберите другое имя.");
        string folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, "." + Path.GetFileName(target) + "." +
            Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temp, lyrics.Replace("\r\n", "\n", StringComparison.Ordinal),
                new UTF8Encoding(false), cancellation);
            cancellation.ThrowIfCancellationRequested();
            File.Move(temp, target, allowOverwrite);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
