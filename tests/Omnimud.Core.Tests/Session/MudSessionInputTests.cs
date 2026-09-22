using FluentAssertions;
using Omnimud.Core.Aliases;
using Omnimud.Core.Paths;
using Omnimud.Core.Session;
using Omnimud.Core.Triggers;

namespace Omnimud.Core.Tests.Session;

public sealed class MudSessionInputTests : IAsyncDisposable
{
    private readonly SessionHarness _h = new();

    public ValueTask DisposeAsync() => _h.DisposeAsync();

    private void AddCompass()
    {
        _h.Store.Directions.AddRange(
        [
            new DirectionEntry("norte", 'n', "sur"),
            new DirectionEntry("sur", 's', "norte"),
            new DirectionEntry("este", 'e', "oeste"),
            new DirectionEntry("oeste", 'o', "este"),
            new DirectionEntry("arriba", 'a', null)
        ]);
    }

    // ── Sending, history, repeat last ──────────────────────────────────────

    [Fact]
    public async Task Command_IsSentWithNewline_NoLocalEcho()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("mirar");

        _h.Connection.SentPackets.Single().Should().Equal("mirar\n"u8.ToArray());
        _h.Lines.Should().BeEmpty();
        _h.Spoken.Should().BeEmpty();
    }

    [Fact]
    public async Task History_KeepsOrder_SkipsConsecutiveDuplicates_AndRespectsSize()
    {
        _h.SetOptions(o => o with { HistorySize = 3 });
        await _h.StartAsync();

        foreach (var c in new[] { "a", "a", "b", "a", "c", "d" })
            await _h.SubmitAsync(c);

        _h.Session.History.Should().Equal("a", "c", "d");
    }

    [Fact]
    public async Task EmptyInput_RepeatsLastCommand_WithoutTouchingHistory()
    {
        await _h.StartAsync();
        await _h.SubmitAsync("atacar orco");

        await _h.SubmitAsync("");
        await _h.SubmitAsync("");

        _h.SentLines.Should().Equal("atacar orco", "atacar orco", "atacar orco");
        _h.Session.History.Should().Equal("atacar orco");
    }

    [Fact]
    public async Task EmptyInput_WithoutLastCommand_SendsBlankLine()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("");

        _h.SentLines.Should().Equal("");
    }

    [Fact]
    public async Task SendBlankLine_SendsJustNewline()
    {
        await _h.StartAsync();
        await _h.SubmitAsync("mirar");

        await _h.Session.SendBlankLineAsync();

        _h.SentLines.Should().Equal("mirar", "");
    }

    [Fact]
    public async Task ExecuteCommand_RunsThePipeline_ButNotHistoryNorLast()
    {
        _h.Store.Aliases.Add(new AliasDefinition("m", "mirar"));
        await _h.StartAsync();
        await _h.SubmitAsync("inventario");

        await _h.Session.ExecuteCommandAsync("m");
        await _h.SubmitAsync("");

        _h.SentLines.Should().Equal("inventario", "mirar", "inventario");
        _h.Session.History.Should().Equal("inventario");
    }

    [Fact]
    public async Task PasswordMode_SendsAsIs_NoHistoryNoLastNoAlias()
    {
        _h.Store.Aliases.Add(new AliasDefinition("clave", "decir mi clave es"));
        await _h.StartAsync();
        await _h.SubmitAsync("mirar");
        await _h.ReceiveBytesAsync(255, 251, 1);
        _h.Connection.ClearSent();

        await _h.SubmitAsync("clave 1234");
        await _h.ReceiveBytesAsync(255, 252, 1);
        _h.Connection.ClearSent();
        await _h.SubmitAsync("");

        _h.Session.History.Should().Equal("mirar");
        _h.SentLines.Should().Equal("mirar"); // the repeat: last command is still "mirar"
    }

    [Fact]
    public async Task PasswordMode_TextGoesOutUntouched()
    {
        _h.SetOptions(o => o with { UseConcatChar = true });
        await _h.StartAsync();
        await _h.ReceiveBytesAsync(255, 251, 1);
        _h.Connection.ClearSent();

        await _h.SubmitAsync("cls;_x");

        _h.SentLines.Should().Equal("cls;_x");
        _h.Clears.Should().Be(0);
    }

    [Fact]
    public async Task Offline_CommandsAreAnswered_ButNothingIsSent()
    {
        await _h.StartAsync(connect: false);
        _h.Session.EnterOfflineMode();
        await _h.Session.WhenIdleAsync();

        await _h.SubmitAsync("mirar");
        await _h.SubmitAsync("-triggers");

        _h.Session.State.Should().Be(SessionState.Offline);
        _h.SystemLines.Should().Equal(
            "Estás en modo de desconexión. No puedes enviar ningún comando.",
            "Sistema de triggers deshabilitado. Utiliza el comando '+triggers' para volver a habilitarlo.");
        _h.Connection.SentPackets.Should().BeEmpty();
    }

    [Fact]
    public async Task Disconnected_SendingTellsTheUser()
    {
        await _h.StartAsync(connect: false);

        await _h.SubmitAsync("mirar");

        _h.SystemLines.Should().Equal("No estás conectado. El comando no se ha enviado.");
    }

    [Fact]
    public async Task SystemReplies_AreAnnounced_AndNeverFireTriggers()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("triggers", "bucle"));
        await _h.StartAsync();

        await _h.SubmitAsync("-triggers");
        await _h.SubmitAsync("+triggers");

        _h.SentLines.Should().BeEmpty();
        _h.Spoken.Should().HaveCount(2);
        _h.Lines.Should().OnlyContain(l => l.Kind == SessionLineKind.System);
    }

    [Fact]
    public async Task EnglishCulture_RepliesInEnglish()
    {
        await using var english = new SessionHarness("en");
        await english.StartAsync(connect: false);
        english.Session.EnterOfflineMode();

        await english.SubmitAsync("mirar");

        english.SystemLines.Should().Equal("You are in offline mode. You cannot send any command.");
    }

    [Fact]
    public async Task SendFailure_IsReportedAsSystemLine()
    {
        await _h.StartAsync();
        _h.Connection.SendFailure = new IOException("tubería rota");

        await _h.SubmitAsync("mirar");

        _h.SystemLines.Should().Equal("Error al enviar: tubería rota");
    }

    [Fact]
    public async Task Latin1Text_WithByte255_IsSentWithTheIacDoubled()
    {
        _h.Profile = _h.Profile with { Encoding = "iso-8859-1" };
        await _h.StartAsync();

        await _h.SubmitAsync("aÿ");

        _h.Connection.SentPackets.Single().Should().Equal((byte)'a', 255, 255, 10);
    }

    [Fact]
    public async Task EvaluationOrder_CommandTriggerBeatsInternalCommand_AndAliasBeatsBoth()
    {
        _h.Store.Triggers.Add(SessionHarness.Trigger("@cls", "decir limpiando"));
        _h.Store.Aliases.Add(new AliasDefinition("-triggers", "decir alias gana"));
        await _h.StartAsync();

        await _h.SubmitAsync("cls");
        await _h.SubmitAsync("-triggers");

        _h.SentLines.Should().Equal("decir limpiando", "decir alias gana");
        _h.Clears.Should().Be(0);
        _h.Session.TriggersEnabled.Should().BeTrue();
    }

    // ── Aliases ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Alias_ReplacesFirstWord_KeepsTheRest_CaseSensitive_OnePass()
    {
        _h.Store.Aliases.AddRange([new AliasDefinition("k", "matar"), new AliasDefinition("matar", "NO")]);
        await _h.StartAsync();

        await _h.SubmitAsync("k orco grande");
        await _h.SubmitAsync("K orco");
        await _h.SubmitAsync("decir k");

        _h.SentLines.Should().Equal("matar orco grande", "K orco", "decir k");
    }

    [Fact]
    public async Task Alias_Disabled_IsIgnored()
    {
        _h.Store.Aliases.Add(new AliasDefinition("k", "matar", Enabled: false));
        await _h.StartAsync();

        await _h.SubmitAsync("k orco");

        _h.SentLines.Should().Equal("k orco");
    }

    [Fact]
    public async Task Alias_CanExpandToInternalCommand_ButNotToPath()
    {
        AddCompass();
        _h.Store.Paths.Add(new PathDefinition("casa", "2n"));
        _h.Store.Aliases.AddRange([new AliasDefinition("limpia", "cls"), new AliasDefinition("ir", "_casa")]);
        await _h.StartAsync();

        await _h.SubmitAsync("limpia");
        await _h.SubmitAsync("ir");

        _h.Clears.Should().Be(1);
        _h.SentLines.Should().Equal("_casa"); // paths are evaluated before aliases
    }

    [Fact]
    public async Task Calias_Query_Add_Duplicate_Remove()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("calias k");
        await _h.SubmitAsync("calias k matar  al orco");
        await _h.SubmitAsync("calias k");
        await _h.SubmitAsync("calias k otra cosa");
        await _h.SubmitAsync("k");
        await _h.SubmitAsync("uncalias k");
        await _h.SubmitAsync("uncalias k");
        await _h.SubmitAsync("k");

        _h.SystemLines.Should().Equal(
            "El alias k no está almacenado.",
            "Alias k insertado correctamente.",
            "El alias k está asignado a la acción matar  al orco.",
            "Ya existe un alias para el comando especificado. Borra ese alias con el comando uncalias, o utiliza otro nombre.",
            "Alias k eliminado correctamente.",
            "No existe ningún alias con ese nombre.");
        _h.SentLines.Should().Equal("matar  al orco", "k");
        _h.Store.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Calias_ValidationMessages()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("calias ");
        await _h.SubmitAsync("calias k  ");
        await _h.SubmitAsync("uncalias");
        await _h.SubmitAsync("uncalias a b");

        _h.SystemLines.Should().Equal(
            "Debes especificar el nombre del alias a consultar.",
            "La acción del alias no puede estar vacía.",
            "Debes especificar el nombre del alias a eliminar.",
            "El nombre del alias no puede contener espacios.");
    }

    [Fact]
    public async Task Calias_SameActionAsAnotherAlias_AsksFirst()
    {
        _h.Store.Aliases.Add(new AliasDefinition("m", "mirar"));
        _h.ConfirmAnswer = false;
        await _h.StartAsync();

        await _h.SubmitAsync("calias l mirar");

        _h.Confirmations.Should().ContainSingle().Which.Should().Contain("m");
        _h.SystemLines.Should().Equal("Alias cancelado.");
        _h.Store.Aliases.Should().HaveCount(1);
    }

    [Fact]
    public async Task Calias_WithoutCharacter_IsRefused_AndStoreFailureIsReported()
    {
        await _h.StartAsync();
        _h.Store.WriteFailure = new InvalidOperationException("db");
        await _h.SubmitAsync("calias k matar");

        await using var noCharacter = new SessionHarness();
        noCharacter.Profile = noCharacter.Profile with { CharacterId = null, CharacterName = null };
        await noCharacter.StartAsync();
        await noCharacter.SubmitAsync("calias k matar");
        await noCharacter.SubmitAsync("calias");
        await noCharacter.SubmitAsync("paths iniciar");

        _h.SystemLines.Should().Equal("¡Error al insertar el alias!");
        noCharacter.SystemLines.Should().OnlyContain(l => l == "Este comando necesita un personaje.").And.HaveCount(3);
    }

    [Fact]
    public async Task WindowCommands_AskTheViewToOpenWindows()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("calias");
        await _h.SubmitAsync("triggers");
        await _h.SubmitAsync("paths");

        _h.Windows.Should().Equal(
            (SessionWindow.Aliases, (string?)null), (SessionWindow.Triggers, null), (SessionWindow.Paths, null));
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Cls_RequestsClear_AndSaysOk()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("cls");

        _h.Clears.Should().Be(1);
        _h.Spoken.Should().Equal("OK");
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task InternalCommands_AreExactMatches()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("cls ");
        await _h.SubmitAsync("CLS");
        await _h.SubmitAsync("paths otra");

        _h.SentLines.Should().Equal("cls ", "CLS", "paths otra");
        _h.Clears.Should().Be(0);
    }

    [Fact]
    public async Task CallateAndHablar_ToggleSilentMode_AndSyncTheEvent()
    {
        var changes = new List<bool>();
        await _h.StartAsync();
        _h.Session.SilentModeChanged += changes.Add;

        await _h.SubmitAsync("callate");
        await _h.SubmitAsync("callate");
        await _h.SubmitAsync("hablar");
        await _h.SubmitAsync("hablar");

        changes.Should().Equal(true, false);
        _h.SystemLines.Should().Equal(
            "Activando modo silencioso.", "El modo silencioso ya estaba activado.",
            "Desactivando modo silencioso.", "El modo silencioso no estaba activado.");
        _h.Spoken.Should().HaveCount(4); // replies are spoken even in silent mode
    }

    [Fact]
    public async Task SilentMode_SetFromTheWindow_IsConfirmedOutLoud_LikeTheCommand()
    {
        // F8 and the menu set the property directly; the change is invisible, so it is announced (original §15.8).
        await _h.StartAsync();

        _h.Session.SilentMode = true;
        _h.Session.SilentMode = true; // no change, no second reply
        _h.Session.SilentMode = false;

        _h.SystemLines.Should().Equal("Activando modo silencioso.", "Desactivando modo silencioso.");
        _h.Spoken.Should().HaveCount(2);
    }

    // ── Paths ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Path_Forward_SendsEveryFullDirectionInOrder()
    {
        AddCompass();
        _h.Store.Paths.Add(new PathDefinition("al bosque", "2ne3s"));
        await _h.StartAsync();

        await _h.SubmitAsync("_al bosque");

        _h.SentLines.Should().Equal("norte", "norte", "este", "sur", "sur", "sur");
    }

    [Fact]
    public async Task Path_Reverse_UsesOppositesInReverseOrder()
    {
        AddCompass();
        _h.Store.Paths.Add(new PathDefinition("bosque", "2ne"));
        await _h.StartAsync();

        await _h.SubmitAsync("_bosque -r");

        _h.SentLines.Should().Equal("oeste", "sur", "sur");
    }

    [Fact]
    public async Task Path_Errors_AreMessages_NeverExceptions()
    {
        AddCompass();
        _h.Store.Paths.AddRange([new PathDefinition("torre", "na"), new PathDefinition("roto", "2x")]);
        await _h.StartAsync();

        await _h.SubmitAsync("_");
        await _h.SubmitAsync("_nada");
        await _h.SubmitAsync("_torre -r");
        await _h.SubmitAsync("_roto");

        _h.SystemLines.Should().Equal(
            "Uso: _nombre_del_path. Ejemplo: _alandel-turman.",
            "No hay ningún path con ese nombre.",
            "No es posible dar la vuelta a este path: alguna de sus direcciones no tiene dirección contraria en el diccionario.",
            "Error: el path no es válido: 2x");
        _h.SentLines.Should().BeEmpty();
    }

    [Fact]
    public async Task Path_WithoutDirections_ExplainsWhy()
    {
        _h.Store.Paths.Add(new PathDefinition("casa", "2n"));
        await _h.StartAsync();

        await _h.SubmitAsync("_casa");

        _h.SystemLines.Single().Should().StartWith("No hay direcciones definidas para este MUD.");
    }

    [Fact]
    public async Task Recording_FullCycle()
    {
        AddCompass();
        await _h.StartAsync();

        await _h.SubmitAsync("paths iniciar");
        await _h.SubmitAsync("n");
        await _h.SubmitAsync("norte");
        await _h.SubmitAsync("mirar");
        await _h.SubmitAsync("e");
        await _h.SubmitAsync("o");
        await _h.SubmitAsync("paths ultima");
        await _h.SubmitAsync("paths ultima borrar");
        await _h.SubmitAsync("paths grabado");
        await _h.SubmitAsync("paths detener");

        _h.SentLines.Should().Equal("n", "norte", "mirar", "e", "o"); // directions are sent as well
        _h.UiSounds.Should().Equal("pop", "pop", "pop", "pop");
        _h.SystemLines.Should().ContainInOrder(
            "La última dirección introducida ha sido: oeste.",
            "Para borrarla, escribe paths ultima borrar.",
            "La última dirección almacenada (oeste), ha sido eliminada del path.",
            "El path que llevas grabado actualmente es:",
            "2ne",
            "Grabación de path detenida.");
        _h.Windows.Should().Equal((SessionWindow.NewPath, "2ne"));
        _h.Session.IsRecordingPath.Should().BeFalse();
        _h.Statuses.Should().Equal("Grabando path", "");
    }

    [Fact]
    public async Task Recording_CommandsWithoutRecording_AreMessages()
    {
        AddCompass();
        await _h.StartAsync();

        foreach (var c in new[] { "paths detener", "paths ultima", "paths ultima borrar", "paths grabado", "paths cancelar" })
            await _h.SubmitAsync(c);

        _h.SystemLines.Should().HaveCount(5).And.OnlyContain(l => l.StartsWith("No estás grabando un path"));
        _h.Windows.Should().BeEmpty();
    }

    [Fact]
    public async Task Recording_StopWithNothingRecorded_KeepsRecording()
    {
        AddCompass();
        await _h.StartAsync();
        await _h.SubmitAsync("paths iniciar");
        _h.ClearOutput();

        await _h.SubmitAsync("paths detener");
        await _h.SubmitAsync("paths iniciar");

        _h.SystemLines[0].Should().Be("Aún no hay direcciones grabadas en este path.");
        _h.SystemLines[1].Should().StartWith("Ya hay iniciada la grabación de un path.");
        _h.Session.IsRecordingPath.Should().BeTrue();
    }

    [Fact]
    public async Task Recording_Cancel_AsksAndClearsWhatWasRecorded()
    {
        AddCompass();
        await _h.StartAsync();
        await _h.SubmitAsync("paths iniciar");
        await _h.SubmitAsync("n");

        _h.ConfirmAnswer = false;
        await _h.SubmitAsync("paths cancelar");
        _h.Session.IsRecordingPath.Should().BeTrue();

        _h.ConfirmAnswer = true;
        await _h.SubmitAsync("paths cancelar");
        await _h.SubmitAsync("paths iniciar");
        await _h.SubmitAsync("s");
        await _h.SubmitAsync("paths grabado");

        _h.Confirmations.Should().HaveCount(2);
        _h.SystemLines.Should().Contain("Grabación de path cancelada.");
        _h.SystemLines[^1].Should().Be("s"); // the cancelled "n" is gone
    }

    [Fact]
    public async Task Recording_RequiresDirections()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("paths iniciar");

        _h.SystemLines.Single().Should().StartWith("No hay direcciones definidas");
        _h.Session.IsRecordingPath.Should().BeFalse();
    }

    // ── Concatenation and repetition ───────────────────────────────────────

    [Fact]
    public async Task Concatenation_Off_SendsTheLineWhole()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("norte;sur");

        _h.SentLines.Should().Equal("norte;sur");
    }

    [Fact]
    public async Task Concatenation_SplitsAndEachPartGoesThroughThePipeline()
    {
        AddCompass();
        _h.Store.Paths.Add(new PathDefinition("casa", "2n"));
        _h.Store.Aliases.Add(new AliasDefinition("m", "mirar"));
        _h.SetOptions(o => o with { UseConcatChar = true });
        await _h.StartAsync();

        await _h.SubmitAsync("sur;m orco;_casa;cls");

        _h.SentLines.Should().Equal("sur", "mirar orco", "norte", "norte");
        _h.Clears.Should().Be(1);
    }

    [Fact]
    public async Task Concatenation_DoubleCharacterIsLiteral_WithTheConfiguredCharacter()
    {
        _h.SetOptions(o => o with { UseConcatChar = true, ConcatChar = '|' });
        await _h.StartAsync();

        await _h.SubmitAsync("decir a||b|mirar;ya");

        _h.SentLines.Should().Equal("decir a|b", "mirar;ya");
    }

    [Fact]
    public async Task Concatenation_AliasActionWithSeparator_IsSplitToo()
    {
        _h.Store.Aliases.Add(new AliasDefinition("prep", "sacar espada;empuñar espada"));
        _h.SetOptions(o => o with { UseConcatChar = true });
        await _h.StartAsync();

        await _h.SubmitAsync("prep");

        _h.SentLines.Should().Equal("sacar espada", "empuñar espada");
    }

    [Fact]
    public async Task Repetition_SendsNTimes_CappedAtFifty()
    {
        _h.SetOptions(o => o with { UseRepeatChar = true });
        await _h.StartAsync();

        await _h.SubmitAsync("3#norte");
        _h.SentLines.Should().Equal("norte", "norte", "norte");

        _h.Connection.ClearSent();
        await _h.SubmitAsync("200#x");
        _h.SentLines.Should().HaveCount(50);
    }

    [Fact]
    public async Task Repetition_Off_OrNotANumber_SendsAsIs()
    {
        await _h.StartAsync();
        await _h.SubmitAsync("3#norte");

        _h.SetOptions(o => o with { UseRepeatChar = true, RepeatChar = '*' });
        await _h.Session.ReloadAsync();
        await _h.SubmitAsync("decir #1");
        await _h.SubmitAsync("2*sur");

        _h.SentLines.Should().Equal("3#norte", "decir #1", "sur", "sur");
    }

    [Fact]
    public async Task ConcatenationAndRepetition_Combine()
    {
        _h.SetOptions(o => o with { UseConcatChar = true, UseRepeatChar = true });
        await _h.StartAsync();

        await _h.SubmitAsync("2#n;mirar");

        _h.SentLines.Should().Equal("n", "n", "mirar");
    }

    [Fact]
    public async Task RecursiveAliasThroughConcatenation_StopsAtTheDepthLimit()
    {
        _h.Store.Aliases.Add(new AliasDefinition("x", "decir hola;x"));
        _h.SetOptions(o => o with { UseConcatChar = true });
        await _h.StartAsync();

        await _h.SubmitAsync("x");

        _h.SentLines.Should().HaveCountLessThanOrEqualTo(12).And.OnlyContain(l => l == "decir hola");
        _h.SystemLines.Should().ContainSingle().Which.Should().StartWith("Demasiados comandos anidados");
    }

    // ── Movement and profile commands ──────────────────────────────────────

    [Fact]
    public async Task Movement_BoundKeyRunsItsCommand_UnboundReturnsFalse()
    {
        _h.Store.Movements[8] = "norte";
        await _h.StartAsync();

        (await _h.Session.ExecuteMovementAsync(8)).Should().BeTrue();
        (await _h.Session.ExecuteMovementAsync(5)).Should().BeFalse();

        _h.SentLines.Should().Equal("norte");
        _h.Session.History.Should().BeEmpty();
    }

    [Fact]
    public async Task MovementMode_IsLoadedAndPersisted()
    {
        _h.Store.MovementMode = true;
        var changes = new List<bool>();
        await _h.StartAsync();
        _h.Session.MovementModeChanged += changes.Add;

        _h.Session.MovementMode.Should().BeTrue();
        await _h.Session.SetMovementModeAsync(false);

        _h.Store.MovementMode.Should().BeFalse();
        _h.Session.MovementMode.Should().BeFalse();
        changes.Should().Equal(false);
    }

    [Fact]
    public async Task SaveAndQuitCommands_ComeFromTheProfile()
    {
        await _h.StartAsync();

        await _h.Session.SendSaveCommandAsync();
        await _h.Session.SendQuitCommandAsync();

        _h.SentLines.Should().Equal("salvar", "abandonar");
    }

    [Fact]
    public async Task SecondsSinceLastActivity_RestartsWithEachSend()
    {
        await _h.StartAsync();
        _h.Time.Advance(TimeSpan.FromSeconds(30));
        _h.Session.SecondsSinceLastActivity.Should().BeApproximately(30, 0.01);

        await _h.SubmitAsync("mirar");
        _h.Time.Advance(TimeSpan.FromSeconds(5));

        _h.Session.SecondsSinceLastActivity.Should().BeApproximately(5, 0.01);
    }

    [Fact]
    public async Task MultilineInput_IsSentAsOneBlock()
    {
        await _h.StartAsync();

        await _h.SubmitAsync("escribir\r\nlínea dos");

        _h.Connection.SentPackets.Single().Should().Equal(System.Text.Encoding.UTF8.GetBytes("escribir\nlínea dos\n"));
    }
}
