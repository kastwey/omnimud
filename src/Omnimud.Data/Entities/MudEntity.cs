namespace Omnimud.Data.Entities;

public sealed class MudEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public bool UseTls { get; set; }
    public bool ValidateCertificate { get; set; } = true;
    public string? SaveCommand { get; set; }
    public string? QuitCommand { get; set; }
    public string? LoginScript { get; set; }
    /// <summary>Legacy name of the original client's rule plugin. Superseded by <see cref="MessageRuleSetId"/>.</summary>
    public string? ProcessRule { get; set; }
    public int? MessageRuleSetId { get; set; }
    public string? SoundDirectory { get; set; }
    public string Encoding { get; set; } = "utf-8";
    /// <summary>Numpad movement mode (F2) remembered for sessions without a character.</summary>
    public bool MovementMode { get; set; }
    /// <summary>Kept in step with <see cref="CharacterEntity.IsDefault"/> by the repositories.</summary>
    public int? DefaultCharacterId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
