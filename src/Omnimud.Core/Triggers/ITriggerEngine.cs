namespace Omnimud.Core.Triggers;

public interface ITriggerEngine
{
    /// <summary>
    /// Evaluates text against all active text triggers. Returns matches in priority order.
    /// </summary>
    IReadOnlyList<TriggerMatch> Process(string text);

    /// <summary>Line triggers (not Multiline, not @command) against one complete line.</summary>
    IReadOnlyList<TriggerMatch> ProcessLine(string line);

    /// <summary>Multiline triggers against a block of lines joined with \n.</summary>
    IReadOnlyList<TriggerMatch> ProcessBlock(string block);

    /// <summary>Command triggers (@word) whose word is <paramref name="firstWord"/>.</summary>
    IReadOnlyList<TriggerDefinition> MatchCommand(string firstWord);

    /// <summary>Loads triggers for the current session.</summary>
    void LoadTriggers(IEnumerable<TriggerDefinition> triggers);

    void EnableTrigger(string id);
    void DisableTrigger(string id);
    void EnableAll();
    void DisableAll();
}
