using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Aliases;
using Omnimud.Core.Scripting;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Session;

public sealed class MudSessionTriggerTests : IAsyncDisposable
{
    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private static ScriptResult Ok() => ScriptResult.Ok([], [], []);

    /// <summary>Makes the fake engine run <paramref name="body"/> as "the script".</summary>
    private void ScriptRuns(Func<ScriptContext, IScriptHost, Task> body)
        => _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await body(call.Arg<ScriptContext>(), call.Arg<IScriptHost>());
                return Ok();
            });

    // ── Line triggers ──────────────────────────────────────────────────────

    [Fact]
    public async Task LineTrigger_SendsItsCommand_WithCaptures()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("%s te dice: '%s'", "decir Anda, %1 me ha dicho %2.", patternType: PatternType.Sscanf));
        await _h.StartAsync();

        await _h.ReceiveAsync("Ana te dice: 'hola'\n");

        _h.SentLines.Should().Equal("decir Anda, Ana me ha dicho hola.");
        _h.PlainLines.Should().Equal("Ana te dice: 'hola'");
    }

    [Fact]
    public async Task LineTrigger_NeedsTheCompleteLine_EvenIfSplitAcrossPackets()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("^El orco ha muerto.$", "coger todo"));
        await _h.StartAsync();

        await _h.ReceiveAsync("El orco ha");
        _h.SentLines.Should().BeEmpty();
        await _h.ReceiveAsync(" muerto.\n");

        _h.SentLines.Should().Equal("coger todo");
    }

    [Fact]
    public async Task LineTrigger_MatchesTextWithoutAnsi()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco rojo", "huir"));
        await _h.StartAsync();

        await _h.ReceiveAsync($"Un {SessionHarness.Esc}[31morco{SessionHarness.Esc}[0m rojo\n");

        _h.SentLines.Should().Equal("huir");
    }

    [Fact]
    public async Task AllMatchingTriggersFire_ByPriorityThenLoadOrder()
    {
        _h.Store.Triggers.AddRange(
        [
            SessionHarness.Trigger("orco", "normal-1", name: "a"),
            SessionHarness.Trigger("orco", "alta", name: "b", priority: 90),
            SessionHarness.Trigger("orco", "normal-2", name: "c"),
            SessionHarness.Trigger("elfo", "no", name: "d")
        ]);
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\n");

        _h.SentLines.Should().Equal("alta", "normal-1", "normal-2");
    }

    [Fact]
    public async Task TriggerFiresOncePerMatchingLine()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "golpear"));
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\nOtro orco\nUn elfo\n");

        _h.SentLines.Should().Equal("golpear", "golpear");
    }

    [Fact]
    public async Task DisabledTrigger_DoesNotFire()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "golpear", enabled: false));
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\n");

        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task GagLine_HidesTheLine_FromScreenLogAndSpeech_ButRulesStillSeeIt()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("[chat]", "", TriggerActionType.PlaySound, gag: true, sound: "chat.wav"));
        _h.Store.Rules.Add(new MessageRule(@"^\[chat\] (.*)$", "$1"));
        await _h.StartAsync();

        await _h.ReceiveAsync("antes\n[chat] Bob: hola\ndespués\n");

        _h.PlainLines.Should().Equal("antes", "después");
        _h.AddedMessages.Select(m => m.Text).Should().Equal("Bob: hola");
        _h.Spoken.Should().Equal("antes", "Bob: hola", "después"); // the message is spoken because its line was not
        _h.Sound.Received(1).PlayTriggerSound("chat.wav");
    }

    [Fact]
    public async Task GaggedBlock_DoesNotFlash()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("spam", "", TriggerActionType.PlaySound, gag: true, sound: "x"));
        await _h.StartAsync(windowActive: false);

        await _h.ReceiveAsync("spam\n");

        _h.Flashes.Should().Be(0);
    }

    [Fact]
    public async Task SubstitutionHandlesTenCaptures()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger(
            @"^(\w) (\w) (\w) (\w) (\w) (\w) (\w) (\w) (\w) (\w)$", "decir %10%1-%9", patternType: PatternType.Regex));
        await _h.StartAsync();

        await _h.ReceiveAsync("a b c d e f g h i j\n");

        _h.SentLines.Should().Equal("decir ja-i");
    }

    [Fact]
    public async Task TriggerAction_GoesThroughTheWholeInputPipeline_ButNotHistory()
    {
        _h.Store.Aliases.Add(new AliasDefinition("k", "matar"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("^%w llega", "k %1;cls", patternType: PatternType.Sscanf));
        _h.SetOptions(o => o with { UseConcatChar = true });
        await _h.StartAsync();

        await _h.ReceiveAsync("Orco llega del norte.\n");

        _h.SentLines.Should().Equal("matar Orco");
        _h.Clears.Should().Be(1);
        _h.Session.History.Should().BeEmpty();
    }

    [Fact]
    public async Task MultilineAction_SendsEachLineAsACommand()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("muerto", "coger todo\r\nenterrar cuerpo"));
        await _h.StartAsync();

        await _h.ReceiveAsync("El orco ha muerto.\n");

        _h.SentLines.Should().Equal("coger todo", "enterrar cuerpo");
    }

    [Fact]
    public async Task SoundAndBothActions_PlayThroughSessionSound()
    {
        _h.Store.Triggers.AddRange(
        [
            SessionHarness.Trigger("campana", "", TriggerActionType.PlaySound, sound: "campana.wav"),
            SessionHarness.Trigger("%s ataca", "huir de %1", TriggerActionType.SendCommandAndPlaySound, PatternType.Sscanf, sound: "alarma.wav")
        ]);
        await _h.StartAsync();

        await _h.ReceiveAsync("Suena una campana\nUn orco ataca\n");

        _h.Sound.Received(1).PlayTriggerSound("campana.wav");
        _h.Sound.Received(1).PlayTriggerSound("alarma.wav"); // the original skipped the sound when there were captures
        _h.SentLines.Should().Equal("huir de Un orco");
    }

    [Fact]
    public async Task SoundAction_WithoutSoundField_PlaysTheActionText()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("alerta", "alerta.wav", TriggerActionType.PlaySound));
        await _h.StartAsync();

        await _h.ReceiveAsync("alerta\n");

        _h.Sound.Received(1).PlayTriggerSound("alerta.wav");
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task SoundThatThrows_DoesNotBreakAnything()
    {
        _h.Sound.PlayTriggerSound(Arg.Any<string>()).Returns(_ => throw new IOException("sin tarjeta de sonido"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("campana", "", TriggerActionType.PlaySound, sound: "c.wav"));
        await _h.StartAsync();

        await _h.ReceiveAsync("campana\notra\n");

        _h.PlainLines.Should().Equal("campana", "otra");
    }

    [Fact]
    public async Task SystemLinesAndPrompts_TriggersOnlySeeMudText()
    {
        _h.Store.Triggers.AddRange([SessionHarness.Trigger("Vida", "curar"), SessionHarness.Trigger("habilitado", "NO")]);
        await _h.StartAsync();

        await _h.ReceiveAsync("Vida: 10> ");
        await _h.AdvanceAsync(150);
        await _h.SubmitAsync("-triggers");
        await _h.SubmitAsync("+triggers");

        _h.SentLines.Should().Equal("curar"); // prompts are MUD text; replies of the client are not
    }

    // ── Multiline triggers ─────────────────────────────────────────────────

    [Fact]
    public async Task MultilineTrigger_SeesTheWholeBlock_OncePerBlock()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger(@"Salidas:\n\s+(\w+)\n\s+(\w+)", "decir %1 y %2", patternType: PatternType.Regex, multiline: true));
        await _h.StartAsync();

        await _h.ReceiveAsync("Salidas:\n  norte\n  sur\n");

        _h.SentLines.Should().Equal("decir norte y sur");
        _h.PlainLines.Should().HaveCount(3);
    }

    [Fact]
    public async Task MultilineTrigger_IsNotEvaluatedPerLine_AndLineTriggerNotPerBlock()
    {
        _h.Store.Triggers.AddRange(
        [
            SessionHarness.Trigger("orco", "bloque", multiline: true),
            SessionHarness.Trigger("orco", "linea")
        ]);
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\nOtro orco\n");

        _h.SentLines.Should().Equal("linea", "linea", "bloque");
    }

    [Fact]
    public async Task MultilineTrigger_GagFlagIsIgnored()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "x", multiline: true, gag: true));
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\n");

        _h.PlainLines.Should().Equal("Un orco");
    }

    // ── Command triggers and the trigger switches ──────────────────────────

    [Fact]
    public async Task CommandTrigger_RunsWithArguments_AndTheCommandIsNotSent()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("@saluda", "decir Hola %1, soy amigo de %2"));
        await _h.StartAsync();

        await _h.SubmitAsync("saluda Ana Bob");

        _h.SentLines.Should().Equal("decir Hola Ana, soy amigo de Bob");
    }

    [Fact]
    public async Task CommandTrigger_IsCheckedAfterAliases_AndRespectsCase()
    {
        _h.Store.Aliases.Add(new AliasDefinition("s", "Saluda"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("@Saluda", "decir hola", caseSensitive: true));
        await _h.StartAsync();

        await _h.SubmitAsync("s");
        await _h.SubmitAsync("saluda");

        _h.SentLines.Should().Equal("decir hola", "saluda");
    }

    [Fact]
    public async Task CommandTrigger_NeverFiresOnMudText()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("@saluda", "decir hola"));
        await _h.StartAsync();

        await _h.ReceiveAsync("@saluda\nsaluda\n");

        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task CommandTrigger_Script_GetsArgsAndFullCommand()
    {
        ScriptContext? seen = null;
        ScriptRuns((ctx, _) => { seen = ctx; return Task.CompletedTask; });
        _h.Store.Triggers.Add(SessionHarness.Trigger("@cura", "-- lua", TriggerActionType.Script, name: "AvisaCura"));
        await _h.StartAsync();

        await _h.SubmitAsync("cura Ana ahora");
        await _h.WaitUntilAsync(() => seen is not null);

        seen!.Captures.Should().Equal("Ana", "ahora");
        seen.FullCommand.Should().Be("cura Ana ahora");
        seen.ScriptName.Should().Be("AvisaCura");
        seen.MudName.Should().Be("Reinos");
        seen.CharacterName.Should().Be("Zork");
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task CommandTriggerCallingItself_StopsAtTheDepthLimit()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("@bucle", "bucle"));
        await _h.StartAsync();

        await _h.SubmitAsync("bucle");

        _h.SystemLines.Should().ContainSingle().Which.Should().StartWith("Demasiados comandos anidados");
    }

    [Fact]
    public async Task MinusTriggers_DisablesLineMultilineAndCommandTriggers_ForTheSessionOnly()
    {
        _h.Store.Triggers.AddRange(
        [
            SessionHarness.Trigger("orco", "linea"),
            SessionHarness.Trigger("orco", "bloque", multiline: true),
            SessionHarness.Trigger("@saluda", "decir hola")
        ]);
        await _h.StartAsync();

        await _h.SubmitAsync("-triggers");
        await _h.SubmitAsync("-triggers");
        await _h.ReceiveAsync("Un orco\n");
        await _h.SubmitAsync("saluda");
        _h.SentLines.Should().Equal("saluda");
        _h.Session.TriggersEnabled.Should().BeFalse();

        await _h.SubmitAsync("+triggers");
        await _h.SubmitAsync("+triggers");
        await _h.ReceiveAsync("Un orco\n");

        _h.SentLines.Should().Equal("saluda", "linea", "bloque");
        _h.SystemLines.Should().Equal(
            "Sistema de triggers deshabilitado. Utiliza el comando '+triggers' para volver a habilitarlo.",
            "El sistema de triggers ya estaba deshabilitado.",
            "Sistema de triggers habilitado. Utiliza el comando '-triggers' para deshabilitarlo.",
            "El sistema de triggers ya estaba habilitado.");
        _h.Store.TriggerWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task MinusTriggerName_DisablesOneTrigger_AndPersistsIt()
    {
        var trigger = SessionHarness.Trigger("orco", "golpear", name: "pegar");
        _h.Store.Triggers.Add(trigger);
        await _h.StartAsync();

        await _h.SubmitAsync("-trigger pegar");
        await _h.SubmitAsync("-trigger pegar");
        await _h.ReceiveAsync("Un orco\n");
        await _h.SubmitAsync("+trigger pegar");
        await _h.SubmitAsync("+trigger pegar");
        await _h.ReceiveAsync("Un orco\n");
        await _h.SubmitAsync("-trigger");
        await _h.SubmitAsync("+trigger");
        await _h.SubmitAsync("+trigger nada");

        _h.SentLines.Should().Equal("golpear");
        _h.Store.TriggerWrites.Should().Equal((trigger.Id, false), (trigger.Id, true));
        _h.SystemLines.Should().Equal(
            "Trigger desactivado.", "El trigger ya está desactivado.",
            "Trigger activado.", "El trigger ya está activado.",
            "¿Desactivar qué trigger?", "¿Activar qué trigger?", "No se encontró el trigger nada.");
    }

    [Fact]
    public async Task MinusTrigger_StoreFailure_IsReported_AndStateUnchanged()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "golpear", name: "pegar"));
        await _h.StartAsync();
        _h.Store.WriteFailure = new InvalidOperationException("db");

        await _h.SubmitAsync("-trigger pegar");
        await _h.ReceiveAsync("Un orco\n");

        _h.SystemLines.Should().Equal("Error al desactivar el trigger.");
        _h.SentLines.Should().Equal("golpear");
    }

    // ── Scripts ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScriptTrigger_RunsWithLineBlockCapturesAndTheSessionAsHost()
    {
        ScriptContext? seen = null;
        IScriptHost? host = null;
        string? code = null;
        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                code = call.ArgAt<string>(0);
                seen = call.Arg<ScriptContext>();
                host = call.Arg<IScriptHost>();
                return Ok();
            });
        _h.Store.Triggers.Add(SessionHarness.Trigger("^%w canta: '%s'$", "om.message(om.captures[1])", TriggerActionType.Script, PatternType.Sscanf));
        await _h.StartAsync();

        await _h.ReceiveAsync("Hace sol.\nAna canta: 'la la'\n");
        await _h.WaitUntilAsync(() => seen is not null);

        code.Should().Be("om.message(om.captures[1])");
        host.Should().BeSameAs(_h.Session);
        seen!.MatchedLine.Should().Be("Ana canta: 'la la'");
        seen.Block.Should().Be("Hace sol.\nAna canta: 'la la'");
        seen.Captures.Should().Equal("Ana", "la la");
        seen.FullCommand.Should().BeNull();
    }

    [Fact]
    public async Task ScriptTrigger_DoesNotBlockReception()
    {
        var release = new TaskCompletionSource();
        ScriptRuns((_, _) => release.Task);
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "lento()", TriggerActionType.Script));
        await _h.StartAsync();

        await _h.ReceiveAsync("Un orco\nsigue llegando texto\n");

        _h.PlainLines.Should().Equal("Un orco", "sigue llegando texto");
        release.SetResult();
    }

    [Fact]
    public async Task SameScriptTrigger_DoesNotOverlapWithItself_ButDifferentOnesRunInParallel()
    {
        var release = new TaskCompletionSource();
        var started = new List<string>();
        ScriptRuns((ctx, _) =>
        {
            lock (started) started.Add(ctx.ScriptName);
            return release.Task;
        });
        _h.Store.Triggers.AddRange(
        [
            SessionHarness.Trigger("orco", "a()", TriggerActionType.Script, name: "uno"),
            SessionHarness.Trigger("orco", "b()", TriggerActionType.Script, name: "dos")
        ]);
        await _h.StartAsync();

        await _h.ReceiveAsync("orco\norco\norco\n");
        await _h.WaitUntilAsync(() => { lock (started) return started.Count >= 2; });
        await Task.Delay(50);
        lock (started) started.Should().BeEquivalentTo(["uno", "dos"]);

        release.SetResult();
        await _h.WaitUntilAsync(() =>
        {
            // Once finished, the same trigger can fire again.
            _h.Connection.RaiseData("orco\n"u8.ToArray()).Wait();
            lock (started) return started.Count >= 4;
        });
    }

    [Fact]
    public async Task ScriptFailure_BecomesASystemLine_NeverAnException()
    {
        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(ScriptResult.Fail("attempt to call a nil value"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "roto()", TriggerActionType.Script, name: "MiTrigger"));
        await _h.StartAsync();

        await _h.ReceiveAsync("orco\n");
        await _h.WaitUntilAsync(() => _h.SystemLines.Count > 0);

        _h.SystemLines.Should().Equal("Error en el script MiTrigger: attempt to call a nil value");
    }

    [Fact]
    public async Task ScriptEngineThatThrows_IsReportedToo()
    {
        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ScriptResult>>(_ => throw new InvalidOperationException("motor roto"));
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "x()", TriggerActionType.Script, name: "T"));
        await _h.StartAsync();

        await _h.ReceiveAsync("orco\nsigue\n");
        await _h.WaitUntilAsync(() => _h.SystemLines.Count > 0);

        _h.SystemLines.Should().Equal("Error en el script T: motor roto");
        _h.MudLines.Should().Equal("orco", "sigue");
    }

    [Fact]
    public async Task ScriptErrorReportedByTheEngine_IsNotShownTwice()
    {
        _h.Scripts.ExecuteAsync(Arg.Any<string>(), Arg.Any<ScriptContext>(), Arg.Any<IScriptHost>(), Arg.Any<ScriptLimits?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<IScriptHost>().ReportScriptError("T", "línea 3: error");
                return ScriptResult.Fail("línea 3: error");
            });
        _h.Store.Triggers.Add(SessionHarness.Trigger("orco", "x()", TriggerActionType.Script, name: "T"));
        await _h.StartAsync();

        await _h.ReceiveAsync("orco\n");
        await _h.WaitUntilAsync(() => _h.SystemLines.Count > 0);
        await Task.Delay(50);
        await _h.Session.WhenIdleAsync();

        _h.SystemLines.Should().Equal("Error en el script T: línea 3: error");
    }

    [Fact]
    public async Task ScriptCallingGag_HidesItsLine()
    {
        ScriptRuns((_, host) => { host.Gag(); return Task.CompletedTask; });
        _h.Store.Triggers.Add(SessionHarness.Trigger("spam", "om.gag()", TriggerActionType.Script));
        await _h.StartAsync();

        await _h.ReceiveAsync("antes\nspam molesto\ndespués\n");

        _h.PlainLines.Should().Equal("antes", "después");
    }

    [Fact]
    public async Task ScriptThatMentionsGagButDoesNotCallIt_LineIsShownInOrder()
    {
        ScriptRuns((_, _) => Task.CompletedTask);
        _h.Store.Triggers.Add(SessionHarness.Trigger("spam", "if false then om.gag() end", TriggerActionType.Script));
        await _h.StartAsync();

        await _h.ReceiveAsync("antes\nspam\ndespués\n");

        _h.PlainLines.Should().Equal("antes", "spam", "después");
    }

    // ── Script host ────────────────────────────────────────────────────────

    [Fact]
    public async Task Host_SendGoesThroughThePipeline_SendRawDoesNot()
    {
        _h.Store.Aliases.Add(new AliasDefinition("k", "matar"));
        await _h.StartAsync();

        _h.Session.Send("k orco");
        _h.Session.SendRaw("k orco");
        await _h.Session.WhenIdleAsync();

        _h.SentLines.Should().Equal("matar orco", "k orco");
        _h.Session.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Host_DisplayBehavesLikeMudText_EchoOnlyPaints()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("dragón", "huir"));
        _h.Store.Rules.Add(new MessageRule("dragón.*", "$0"));
        await _h.StartAsync();

        _h.Session.Display($"{SessionHarness.Esc}[31mUn dragón{SessionHarness.Esc}[0m\nsegunda");
        await _h.Session.WhenIdleAsync();
        _h.SentLines.Should().Equal("huir");
        _h.Spoken.Should().Equal("Un dragón", "segunda");
        _h.AddedMessages.Should().ContainSingle();
        _h.PlainLines.Should().Equal("Un dragón", "segunda");

        _h.ClearOutput();
        _h.Session.Echo("Un dragón pintado");
        await _h.Session.WhenIdleAsync();

        _h.PlainLines.Should().Equal("Un dragón pintado");
        _h.SentLines.Should().BeEmpty();
        _h.Spoken.Should().BeEmpty();
        _h.AddedMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Host_DisplayColors_DoNotLeakIntoMudColors()
    {
        await _h.StartAsync();

        _h.Session.Display($"{SessionHarness.Esc}[31mrojo sin cerrar");
        await _h.Session.WhenIdleAsync();
        await _h.ReceiveAsync("texto del mud\n");

        _h.Lines[^1].Segments.Single().Style.Should().Be(Omnimud.Core.Text.AnsiStyle.Default);
    }

    [Fact]
    public async Task Host_Say_AddMessage_Status_Variables()
    {
        await _h.StartAsync(windowActive: false);

        _h.Session.Say($"{SessionHarness.Esc}[1mcuidado", interrupt: true);
        _h.Session.AddMessage("nota del script");
        _h.Session.SetStatus("PV 10/20");
        _h.Session.SetVariable("dormido", "si");
        await _h.Session.WhenIdleAsync();

        _h.Announcements.Should().Equal(("cuidado", AnnouncePriority.Interrupt));
        _h.AddedMessages.Single().Text.Should().Be("nota del script");
        _h.Statuses.Should().Equal("PV 10/20");
        _h.Session.GetVariable("dormido").Should().Be("si");
        _h.Session.IsVariableSet("dormido").Should().BeTrue();
        _h.Session.RemoveVariable("dormido").Should().BeTrue();
        _h.Session.RemoveVariable("dormido").Should().BeFalse();
        _h.Session.GetVariable("dormido").Should().BeNull();
    }

    [Fact]
    public async Task Host_PlayAndStopSound_UseSessionSound()
    {
        _h.Sound.StopTriggerSound("reloj.mp3").Returns(true);
        await _h.StartAsync();

        _h.Session.PlaySound("reloj.mp3", -1, 80, 60);

        _h.Sound.Received(1).PlayTriggerSound("reloj.mp3", -1, 80, 60);
        _h.Session.StopSound("reloj.mp3").Should().BeTrue();
        _h.Session.StopSound("otro").Should().BeFalse();
    }
}
