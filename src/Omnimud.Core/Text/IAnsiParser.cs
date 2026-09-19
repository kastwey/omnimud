namespace Omnimud.Core.Text;

public interface IAnsiParser
{
    /// <summary>
    /// Parses text containing ANSI escape codes and returns styled segments.
    /// </summary>
    IReadOnlyList<StyledSegment> Parse(string text);
}
