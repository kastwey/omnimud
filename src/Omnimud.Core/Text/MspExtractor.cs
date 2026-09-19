using System.Globalization;
using System.Text.RegularExpressions;

namespace Omnimud.Core.Text;

/// <summary>Where <see cref="MspExtractor"/> looks for MSP commands.</summary>
public enum MspRecognition
{
    /// <summary>Only at the start of a line, as the MSP standard and the original client require.
    /// Anything else would let another player trigger sounds and downloads from a chat line.</summary>
    LineStart,

    /// <summary>Anywhere in the text (legacy behaviour of the first v2 drafts).</summary>
    Anywhere
}

/// <summary>
/// Extracts !!SOUND(...) and !!MUSIC(...) MSP commands from text.
/// </summary>
public sealed partial class MspExtractor
{
    private const string CommandPattern = @"!!(?<type>SOUND|MUSIC)\((?<args>[^)\n]*)\)";

    [GeneratedRegex(CommandPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnywhereRegex();

    // One or more commands glued together at the very start of a line.
    [GeneratedRegex(@"^(?:" + CommandPattern + @")+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex LineStartRegex();

    private readonly MspRecognition _recognition;

    public MspExtractor() : this(MspRecognition.LineStart) { }

    public MspExtractor(MspRecognition recognition) => _recognition = recognition;

    /// <summary>True if the line starts with an MSP command.</summary>
    public static bool IsMspLine(string? line) =>
        !string.IsNullOrEmpty(line) && line.StartsWith("!!", StringComparison.Ordinal) && LineStartRegex().IsMatch(line);

    /// <summary>
    /// Extracts MSP commands from text, returning the clean text and extracted commands.
    /// The rest of the text is returned untouched.
    /// </summary>
    public (string CleanText, IReadOnlyList<SoundCommand> Commands) Extract(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("!!", StringComparison.Ordinal))
            return (text, []);

        var commands = new List<SoundCommand>();
        string clean;

        if (_recognition == MspRecognition.Anywhere)
        {
            clean = AnywhereRegex().Replace(text, match =>
            {
                AddCommand(commands, match.Groups["type"].Value, match.Groups["args"].Value);
                return string.Empty;
            });
        }
        else
        {
            clean = LineStartRegex().Replace(text, match =>
            {
                var types = match.Groups["type"].Captures;
                var args = match.Groups["args"].Captures;
                for (var i = 0; i < types.Count; i++)
                    AddCommand(commands, types[i].Value, args[i].Value);
                return string.Empty;
            });
        }

        return (clean, commands);
    }

    private static void AddCommand(List<SoundCommand> commands, string type, string args)
    {
        var command = ParseCommand(type, args);
        if (command is not null)
            commands.Add(command);
    }

    private static SoundCommand? ParseCommand(string typeText, string argsText)
    {
        var type = typeText.Equals("MUSIC", StringComparison.OrdinalIgnoreCase)
            ? SoundType.Music
            : SoundType.Sound;

        var parts = argsText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return null;

        string? fileName = null;
        var volume = 100;
        var loop = 1;
        var priority = 50;
        // MSP: music continues by default when the same file is requested again; sounds always restart.
        var cont = type == SoundType.Music;
        string? category = null;
        string? url = null;

        foreach (var part in parts)
        {
            if (part.Length < 2 || part[1] != '=' || !IsParameterLetter(part[0]))
            {
                // The file name is the first token that is not a parameter, wherever it comes.
                fileName ??= part;
                continue;
            }

            var value = part[2..];
            switch (char.ToUpperInvariant(part[0]))
            {
                case 'V' when TryParseInt(value, out var v):
                    volume = Math.Clamp(v, 0, 100);
                    break;
                case 'L' when TryParseInt(value, out var l):
                    loop = l;
                    break;
                case 'P' when TryParseInt(value, out var p):
                    priority = Math.Clamp(p, 0, 100);
                    break;
                case 'C':
                    if (value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
                        cont = true;
                    else if (value is "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                        cont = false;
                    break;
                case 'T' when value.Length > 0:
                    category = value;
                    break;
                case 'U' when value.Length > 0:
                    url = value;
                    break;
            }
        }

        if (fileName is null)
            return null;

        if (fileName.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            // "off" with U= announces the default download URL of the MUD.
            return new SoundCommand { Type = type, FileName = string.Empty, IsStop = true, Url = url };
        }

        return new SoundCommand
        {
            Type = type,
            FileName = fileName,
            Volume = volume,
            Loop = loop,
            Priority = priority,
            Continue = cont,
            SoundCategory = category,
            Url = url
        };
    }

    private static bool IsParameterLetter(char c) => char.ToUpperInvariant(c) is 'V' or 'L' or 'P' or 'C' or 'T' or 'U';

    private static bool TryParseInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
}
