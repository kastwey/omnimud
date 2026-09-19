using System.IO.Enumeration;

namespace Omnimud.Core.Sound;

/// <summary>
/// Turns a sound name that comes from outside (the MUD, a trigger, a script) into a path that is
/// guaranteed to stay inside a base folder. Subfolders are allowed; leaving the folder is not.
/// </summary>
public static class SoundPathResolver
{
    private static readonly string[] s_extensions = [".wav", ".mp3", ".ogg"];
    private static readonly char[] s_separators = ['/', '\\'];

    /// <summary>Playable extensions, in lookup order.</summary>
    public static IReadOnlyList<string> PlayableExtensions => s_extensions;

    public static bool IsPlayableExtension(string? extension) =>
        extension is not null && s_extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    public static bool HasWildcards(string name) => name.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>
    /// Relative, normalised form of <paramref name="name"/> (optionally under <paramref name="subfolder"/>)
    /// as path segments, with ".wav" appended when there is no playable extension.
    /// Null if the name is rooted, has a drive, climbs with "..", or has characters that cannot be in a file name.
    /// Wildcards are only accepted in the last segment.
    /// </summary>
    public static string[]? NormalizeSegments(string? subfolder, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var segments = new List<string>();
        if (!string.IsNullOrWhiteSpace(subfolder) && !AddSegments(segments, subfolder.Trim()))
            return null;
        if (HasWildcards(string.Concat(segments)))
            return null;

        var directoryCount = segments.Count;
        if (!AddSegments(segments, name.Trim()) || segments.Count == directoryCount)
            return null;

        for (var i = 0; i < segments.Count - 1; i++)
        {
            if (HasWildcards(segments[i]))
                return null;
        }

        var last = segments[^1];
        var extension = Path.GetExtension(last);
        if (!IsPlayableExtension(extension) && extension != ".*")
            segments[^1] = last + ".wav";

        return [.. segments];
    }

    /// <summary>
    /// Full path of the sound inside <paramref name="baseDirectory"/>, or null if it would fall outside.
    /// The result may still contain wildcards: pass it to <see cref="FindMatches"/>.
    /// </summary>
    public static string? Combine(string? baseDirectory, string? subfolder, string? name)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return null;

        var segments = NormalizeSegments(subfolder, name);
        if (segments is null)
            return null;

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
            var full = Path.GetFullPath(Path.Combine([root, .. segments]));

            // Last line of defence: whatever the normalisation above let through, the result stays inside
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Existing playable files for a full path that may carry '*' and '?' in its file name,
    /// sorted by name. Without wildcards: the file itself if it exists.
    /// </summary>
    public static IReadOnlyList<string> FindMatches(string fullPath)
    {
        try
        {
            var fileName = Path.GetFileName(fullPath);
            if (!HasWildcards(fileName))
                return File.Exists(fullPath) ? [fullPath] : [];

            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return [];

            // Not Directory.GetFiles(pattern): Win32 matching also looks at 8.3 names and treats "*.wav" as "*.wav*"
            return Directory.EnumerateFiles(directory)
                .Where(f => FileSystemName.MatchesSimpleExpression(fileName, Path.GetFileName(f), ignoreCase: true)
                            && IsPlayableExtension(Path.GetExtension(f)))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool AddSegments(List<string> segments, string path)
    {
        // Rooted ("\x", "/x", "\\server\share"), drive-qualified ("C:x") and alternate streams ("x:y") are out
        if (path.Contains(':') || path[0] is '/' or '\\')
            return false;

        foreach (var segment in path.Split(s_separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or ".." || segment[^1] is '.' or ' ')
                return false;

            foreach (var c in segment)
            {
                if (c is '*' or '?')
                    continue;
                if (char.IsControl(c) || c is '"' or '<' or '>' or '|')
                    return false;
            }

            segments.Add(segment);
        }

        return true;
    }
}
