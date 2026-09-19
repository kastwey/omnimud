using System.Text.Json;
using Omnimud.Core.Resources;
using Omnimud.Core.Scripting;
using Omnimud.Core.Telnet;
using Omnimud.Core.Text;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Session;

// Reception: bytes → telnet → text → lines → MSP, om.get, ANSI, triggers, display, log, speech, messages.
public sealed partial class MudSession
{
    private readonly record struct IncomingLine(string Text, SessionLineKind Kind);

    private async Task ProcessDataAsync(byte[] data)
    {
        if (_state != SessionState.Connected)
            return;

        var parsed = _telnet.Process(data);

        foreach (var negotiation in parsed.Negotiations)
            await HandleNegotiationAsync(negotiation).ConfigureAwait(false);

        foreach (var gmcp in parsed.GmcpMessages)
            HandleGmcp(gmcp);

        if (parsed.CleanData.Length > 0 || parsed.EndsWithPromptMark)
        {
            var text = _decoder.Decode(parsed.CleanData.Span);
            var complete = _lineBuffer.Append(text);

            var block = new List<IncomingLine>(complete.Count + 1);
            foreach (var line in complete)
                block.Add(new IncomingLine(line, SessionLineKind.Mud));

            // GA / EOR: the server says the prompt is complete, no need to wait for the timer.
            if (parsed.EndsWithPromptMark && _lineBuffer.TakePending() is { } prompt)
                block.Add(new IncomingLine(prompt, SessionLineKind.Prompt));

            if (block.Count > 0)
                await ProcessBlockAsync(block, injected: false).ConfigureAwait(false);
        }

        ArmGetQuietTimer();
    }

    private void OnPromptTimeout(long generation)
        => _queue.Post(async () =>
        {
            if (_lineBuffer.TakePendingIf(generation) is { } prompt)
                await ProcessBlockAsync([new IncomingLine(prompt, SessionLineKind.Prompt)], injected: false).ConfigureAwait(false);
        }, ReportInternalError);

    /// <summary>Whatever is waiting for a newline is shown now (disconnection, om.get about to start).</summary>
    private async Task FlushPendingTextAsync()
    {
        if (_lineBuffer.TakePending() is { } pending)
            await ProcessBlockAsync([new IncomingLine(pending, SessionLineKind.Prompt)], injected: false).ConfigureAwait(false);
    }

    // ── Telnet ─────────────────────────────────────────────────────────────

    private async Task HandleNegotiationAsync(TelnetNegotiation negotiation)
    {
        // With negotiation off the sequences are only stripped, as the original client did.
        if (!_options.TelnetNegotiation)
            return;

        var (verb, option) = (negotiation.Verb, negotiation.Command);

        if (option == TelnetCommand.Echo && verb is TelnetVerb.Will or TelnetVerb.Wont)
        {
            // Server-side echo means a password is being typed.
            var on = verb == TelnetVerb.Will;
            await SendBytesAsync(_telnet.BuildResponse(option, on ? TelnetVerb.Do : TelnetVerb.Dont)).ConfigureAwait(false);
            SetPasswordMode(on);
            return;
        }

        // Answer each request once per connection: a server that insists must not start a loop.
        if (!_answeredOptions.Add((verb, option)))
            return;

        switch (verb)
        {
            case TelnetVerb.Will when option == TelnetCommand.Gmcp:
                await SendBytesAsync(_telnet.BuildResponse(option, TelnetVerb.Do)).ConfigureAwait(false);
                await SendBytesAsync(TelnetNegotiator.BuildGmcpPacket("Core.Hello", "{\"client\":\"OMnimud\",\"version\":\"2\"}")).ConfigureAwait(false);
                await SendBytesAsync(TelnetNegotiator.BuildGmcpPacket("Core.Supports.Set", SupportedGmcpModules())).ConfigureAwait(false);
                break;

            case TelnetVerb.Will when option is TelnetCommand.EndOfRecord or TelnetCommand.SuppressGoAhead:
                await SendBytesAsync(_telnet.BuildResponse(option, TelnetVerb.Do)).ConfigureAwait(false);
                break;

            case TelnetVerb.Will:
                await SendBytesAsync(_telnet.BuildResponse(option, TelnetVerb.Dont)).ConfigureAwait(false);
                break;

            case TelnetVerb.Do:
                await SendBytesAsync(_telnet.BuildResponse(option, TelnetVerb.Wont)).ConfigureAwait(false);
                break;

            // WONT / DONT are the server agreeing with what we never enabled: nothing to say.
        }
    }

    private GmcpEchoFilter? _gmcpEcho;
    private GmcpEchoFilter GmcpEcho => _gmcpEcho ??= new GmcpEchoFilter(_time);

    /// <summary>Channels, plus whatever the actions menu knows how to translate (Char.Inventory, Room.Info...).</summary>
    private string SupportedGmcpModules()
        => JsonSerializer.Serialize(new[] { "Comm.Channel 1" }.Concat(_actionMenu.SupportedModules));

    private void HandleGmcp(GmcpMessage gmcp)
    {
        // Packages that describe what can be done (inventory, exits): they only refresh the actions menu.
        // Nothing is shown or announced; untrusted content is cleaned and limited inside ActionMenuState.
        if (_actionMenu.Update(gmcp.Package, gmcp.Payload))
        {
            Raise(ActionMenuChanged);
            return;
        }

        if (!gmcp.Package.Equals("Comm.Channel.Text", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            using var document = JsonDocument.Parse(gmcp.Payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            var channel = ReadString(root, "channel");
            var sender = ReadString(root, "talker") is { Length: > 0 } talker ? talker : ReadString(root, "sender");
            var text = AnsiParser.Strip(ReadString(root, "text")).Trim('\r', '\n');
            if (text.Length == 0) return;

            var formatted = sender.Length > 0 ? $"[{channel}] {sender}: {text}" : $"[{channel}] {text}";
            // The MUD usually prints the same message as text too: say it only once (GmcpEchoFilter).
            var alreadySpoken = GmcpEcho.NoteGmcpMessage(text, sender);
            AddMessageCore(formatted, channel, sender.Length > 0 ? sender : null, alreadyAnnounced: alreadySpoken);
        }
        catch (JsonException)
        {
            // Malformed GMCP is not worth a message to the user.
        }

        static string ReadString(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
    }

    // ── Lines ──────────────────────────────────────────────────────────────

    private async Task ProcessBlockAsync(IReadOnlyList<IncomingLine> block, bool injected)
    {
        // Injected text (om.display) carries its own colours and must not disturb the MUD's.
        var ansi = injected ? new AnsiParser() : _ansi;
        var plainLines = new List<string>(block.Count);
        // Only filled for a rule set of type script: what the script sees, and what happened to each line.
        var scripted = !injected && _ruleScript is not null;
        List<ScriptedLine>? scriptLines = scripted ? new List<ScriptedLine>(block.Count) : null;
        var anyVisible = false;
        string? blockText = null;

        string BlockText() => blockText ??= string.Join('\n', block.Select(l => AnsiParser.Strip(l.Text).Replace(SpeakMarker, string.Empty)));

        foreach (var incoming in block)
        {
            var raw = incoming.Text;

            if (TryHandleMsp(raw))
                continue;

            if (!injected && _activeGet is { } get && await TryCaptureAsync(get, raw, incoming.Kind).ConfigureAwait(false))
                continue;

            var parsed = ansi.ParseLine(raw);
            var plain = parsed.PlainText;
            var segments = parsed.Segments;

            // "all_speak:" never reaches the screen or the log; in silent mode what follows is spoken.
            string? forcedSpeech = null;
            var marker = plain.IndexOf(SpeakMarker, StringComparison.Ordinal);
            if (marker >= 0)
            {
                forcedSpeech = plain[(marker + SpeakMarker.Length)..];
                plain = plain.Remove(marker, SpeakMarker.Length);
                segments = RemoveRange(segments, marker, SpeakMarker.Length);
            }

            var gagged = false;
            if (plain.Length > 0)
            {
                foreach (var match in _triggers.ProcessLine(plain))
                    gagged |= await FireTriggerAsync(match.Trigger, match.Captures, plain, BlockText, null, 0, canGag: true).ConfigureAwait(false);
            }

            var announced = false;
            var gmcpCopy = false;
            if (!gagged)
            {
                Raise(LineReceived, new SessionLine(segments, plain, incoming.Kind));
                _log.WriteLine(plain);
                // The text copy of a GMCP channel message: it is shown and logged like any line, but the
                // message already spoke for it (when messages are announced at all) and already is in Messages.
                gmcpCopy = GmcpEcho.IsEchoOfGmcpMessage(plain);
                announced = (gmcpCopy && _options.AnnounceMessages) || AnnounceMudLine(plain, forcedSpeech);
                if (announced && !gmcpCopy) GmcpEcho.NoteSpokenLine(plain);
                anyVisible = true;
            }

            if (scripted)
            {
                // Prompts are not part of a message; the original rules saw them only as trailing noise.
                if (incoming.Kind == SessionLineKind.Mud)
                    scriptLines!.Add(new ScriptedLine(plain, announced, gmcpCopy));
            }
            else if (!gmcpCopy && _rules.Match(plain) is { } message)
            {
                AddMessageCore(message.Text, message.Channel, message.Sender, announced);
            }

            plainLines.Add(plain);
        }

        if (scriptLines is { Count: > 0 })
            await RunMessageScriptAsync(scriptLines).ConfigureAwait(false);

        if (plainLines.Count > 0)
        {
            var text = string.Join('\n', plainLines);
            foreach (var match in _triggers.ProcessBlock(text))
                await FireTriggerAsync(match.Trigger, match.Captures, match.MatchedText, () => text, null, 0, canGag: false).ConfigureAwait(false);
        }

        if (anyVisible && !_windowActive && _options.FlashWindow)
            Raise(FlashRequested);
    }

    private bool TryHandleMsp(string raw)
    {
        if (!raw.Contains("!!", StringComparison.Ordinal))
            return false;

        var probe = AnsiParser.Strip(raw);
        if (!probe.StartsWith("!!SOUND(", StringComparison.OrdinalIgnoreCase) &&
            !probe.StartsWith("!!MUSIC(", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var (_, commands) = _msp.Extract(probe);
            foreach (var command in commands)
                _ = PlayMspAsync(command);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }

        // MSP lines never reach the screen, even when sound is off or the command is malformed.
        return true;
    }

    private async Task PlayMspAsync(SoundCommand command)
    {
        try
        {
            await _sound.HandleMspAsync(command, _cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A missing file or a failed download is not the user's problem mid-game.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    /// <summary>Returns true if the line was spoken (so a message made from it is not spoken again).</summary>
    private bool AnnounceMudLine(string plain, string? forcedSpeech)
    {
        if (!_windowActive)
            return false;

        if (_silentMode)
        {
            if (string.IsNullOrWhiteSpace(forcedSpeech)) return false;
            RaiseAnnounce(forcedSpeech, AnnouncePriority.Queue);
            return true;
        }

        if (!_options.AnnounceMudText || string.IsNullOrWhiteSpace(plain))
            return false;

        RaiseAnnounce(plain, AnnouncePriority.Queue);
        return true;
    }

    private void RaiseAnnounce(string text, AnnouncePriority priority)
    {
        var handler = Announce;
        if (handler is null) return;

        var clean = AnsiParser.Strip(text).Replace(SpeakMarker, string.Empty);
        if (string.IsNullOrWhiteSpace(clean)) return;

        foreach (var target in handler.GetInvocationList())
        {
            try { ((Action<string, AnnouncePriority>)target)(clean, priority); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
    }

    private void AddMessageCore(string text, string? channel, string? sender, bool alreadyAnnounced)
    {
        var message = _messages.Add(_time.GetLocalNow().DateTime, text, channel, sender);
        Raise(MessageAdded, message);

        if (!alreadyAnnounced && _options.AnnounceMessages && _windowActive && !_silentMode)
            RaiseAnnounce(text, AnnouncePriority.Queue);
    }

    /// <summary>A reply of the client itself: painted, logged and announced; never run through triggers.</summary>
    private void WriteSystem(string text, AnnouncePriority priority)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            Raise(LineReceived, new SessionLine([new StyledSegment(line, AnsiStyle.Default)], line, SessionLineKind.System));
            _log.WriteLine(line);
        }

        // The user asked for this, so it is spoken even in silent mode.
        if (_windowActive)
            RaiseAnnounce(text, priority);
    }

    /// <summary>om.display: as if it came from the MUD.</summary>
    private Task ProcessInjectedAsync(string text)
    {
        var buffer = new System.Text.StringBuilder(text.Length);
        var block = new List<IncomingLine>();
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n':
                    block.Add(new IncomingLine(buffer.ToString(), SessionLineKind.Mud));
                    buffer.Clear();
                    break;
                case '\r':
                    break;
                case '\b':
                    if (buffer.Length > 0) buffer.Length--;
                    break;
                default:
                    buffer.Append(c);
                    break;
            }
        }
        if (buffer.Length > 0 || block.Count == 0)
            block.Add(new IncomingLine(buffer.ToString(), SessionLineKind.Mud));

        return ProcessBlockAsync(block, injected: true);
    }

    /// <summary>om.echo: paint only.</summary>
    private void EchoCore(string text)
    {
        var ansi = new AnsiParser();
        foreach (var line in text.Replace("\r", string.Empty).Split('\n'))
        {
            var parsed = ansi.ParseLine(line);
            Raise(LineReceived, new SessionLine(parsed.Segments, parsed.PlainText, SessionLineKind.Mud));
        }
    }

    private static IReadOnlyList<StyledSegment> RemoveRange(IReadOnlyList<StyledSegment> segments, int start, int length)
    {
        var result = new List<StyledSegment>(segments.Count);
        var position = 0;
        var end = start + length;

        foreach (var segment in segments)
        {
            var segmentStart = position;
            var segmentEnd = position + segment.Text.Length;
            position = segmentEnd;

            if (segmentEnd <= start || segmentStart >= end)
            {
                result.Add(segment);
                continue;
            }

            var keepHead = Math.Max(0, start - segmentStart);
            var dropTo = Math.Min(segment.Text.Length, end - segmentStart);
            var text = segment.Text[..keepHead] + segment.Text[dropTo..];
            if (text.Length > 0)
                result.Add(segment with { Text = text });
        }

        return result;
    }

    // ── Trigger actions ────────────────────────────────────────────────────

    /// <summary>Runs one trigger. Returns true if the line that fired it must be hidden.</summary>
    private async Task<bool> FireTriggerAsync(
        TriggerDefinition trigger,
        IReadOnlyList<string> captures,
        string line,
        Func<string> block,
        string? fullCommand,
        int depth,
        bool canGag)
    {
        var gag = canGag && trigger.GagLine;

        try
        {
            switch (trigger.ActionType)
            {
                case TriggerActionType.SendCommand:
                    await RunTriggerCommandAsync(trigger, captures, depth).ConfigureAwait(false);
                    break;

                case TriggerActionType.PlaySound:
                    PlayTriggerSound(string.IsNullOrWhiteSpace(trigger.Sound) ? trigger.Action : trigger.Sound);
                    break;

                case TriggerActionType.SendCommandAndPlaySound:
                    PlayTriggerSound(trigger.Sound);
                    await RunTriggerCommandAsync(trigger, captures, depth).ConfigureAwait(false);
                    break;

                case TriggerActionType.Script:
                    var context = new ScriptContext
                    {
                        ScriptName = trigger.Name,
                        MatchedLine = line,
                        Block = block(),
                        Captures = captures,
                        FullCommand = fullCommand,
                        MudName = Profile.MudName ?? Profile.Title,
                        CharacterName = Profile.CharacterName
                    };

                    var run = StartScript(trigger, context);
                    if (run is not null && canGag && !gag && trigger.Action.Contains("gag", StringComparison.OrdinalIgnoreCase))
                    {
                        // Only a script that may call om.gag holds its line back, and only briefly.
                        await Task.WhenAny(run.Completion, run.GagDecided, Task.Delay(_settings.ScriptGagWait)).ConfigureAwait(false);
                        gag = run.CloseGagWindow();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteSystem(string.Format(Strings.Session_ScriptError, trigger.Name, ex.Message), AnnouncePriority.Queue);
        }

        return gag;
    }

    private async Task RunTriggerCommandAsync(TriggerDefinition trigger, IReadOnlyList<string> captures, int depth)
    {
        var command = TriggerCaptures.Substitute(trigger.Action, captures);
        foreach (var line in command.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length > 0)
                await _input.ExecuteAsync(line, depth + 1).ConfigureAwait(false);
        }
    }

    private void PlayTriggerSound(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Guard(() => _sound.PlayTriggerSound(name));
    }

    private ScriptRun? StartScript(TriggerDefinition trigger, ScriptContext context)
    {
        lock (_runningScripts)
        {
            // The same trigger never overlaps with itself: this firing is dropped.
            if (!_runningScripts.Add(trigger.Id))
                return null;
        }

        var run = new ScriptRun();
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            ScriptRun.Current.Value = run;
            try
            {
                var result = await _scripts.ExecuteAsync(trigger.Action, context, this, null, token).ConfigureAwait(false);
                if (result is { Success: false } && !run.ErrorReported)
                    ReportScriptError(trigger.Name, result.Error ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!run.ErrorReported)
                    ReportScriptError(trigger.Name, ex.Message);
            }
            finally
            {
                lock (_runningScripts)
                    _runningScripts.Remove(trigger.Id);
                run.Complete();
            }
        }, CancellationToken.None);

        return run;
    }

    /// <summary>State of one script execution, visible from the host calls it makes.</summary>
    private sealed class ScriptRun
    {
        public static readonly AsyncLocal<ScriptRun?> Current = new();

        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _gagDecided = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private bool _gagged;
        private bool _windowClosed;

        public Task Completion => _completion.Task;
        public Task GagDecided => _gagDecided.Task;
        public volatile bool ErrorReported;

        public void Gag()
        {
            lock (_gate)
            {
                if (_windowClosed) return;
                _gagged = true;
            }
            _gagDecided.TrySetResult();
        }

        /// <summary>The script is going to wait for the session (om.get): it cannot gag in time any more.</summary>
        public void ReleaseLine() => _gagDecided.TrySetResult();

        public bool CloseGagWindow()
        {
            lock (_gate)
            {
                _windowClosed = true;
                return _gagged;
            }
        }

        public void Complete() => _completion.TrySetResult();
    }
}
