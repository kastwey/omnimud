namespace Omnimud.Core.Text;

/// <summary>
/// Represents an MSP sound/music command extracted from MUD output.
/// </summary>
public sealed record SoundCommand
{
    public required SoundType Type { get; init; }
    public required string FileName { get; init; }
    public int Volume { get; init; } = 100;
    public int Loop { get; init; } = 1;
    public int Priority { get; init; } = 50;
    public bool Continue { get; init; }
    public string? SoundCategory { get; init; }
    public string? Url { get; init; }
    public bool IsStop { get; init; }
}

/// <summary>
/// Sound categories. Each one has its own priority table; <see cref="Trigger"/> follows the
/// switches of <see cref="Sound"/> but never competes with MSP sounds or music.
/// </summary>
public enum SoundType
{
    Sound,
    Music,
    Trigger
}
