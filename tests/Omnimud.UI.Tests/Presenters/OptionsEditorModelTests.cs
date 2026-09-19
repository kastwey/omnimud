using System.Globalization;
using System.Reflection;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Presenters;

public sealed class OptionsEditorModelTests
{
    private const int MudId = 3;
    private const int CharacterId = 7;

    private readonly FakeOptionsService _service = new();
    private readonly IUserPrompts _prompts = Substitute.For<IUserPrompts>();
    private readonly IOptionsFileStore _files = Substitute.For<IOptionsFileStore>();
    private readonly IDirectoryAccess _directories = Substitute.For<IDirectoryAccess>();

    public OptionsEditorModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _directories.Exists(Arg.Any<string>()).Returns(true);
    }

    private OptionsEditorModel Model(OptionScope scope, int? id = null, string? name = null, int? parentMudId = null) =>
        new(_service, scope, id, name, parentMudId, _files, _directories);

    private OptionsEditorModel Loaded(OptionScope scope, int? id = null, string? name = null, int? parentMudId = null)
    {
        var model = Model(scope, id, name, parentMudId);
        model.LoadAsync().GetAwaiter().GetResult();
        return model;
    }

    private static OptionsFields ValidFields() => OptionsFields.From(OmnimudOptions.Default);

    // ───────────────────────────── Construction ─────────────────────────────

    [Fact]
    public void MudAndCharacterScopes_NeedAnId()
    {
        var mud = () => Model(OptionScope.Mud);
        var character = () => Model(OptionScope.Character);

        mud.Should().Throw<ArgumentException>();
        character.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Titles_SayTheScope()
    {
        Model(OptionScope.Global).Title.Should().Be("Opciones globales");
        Model(OptionScope.Mud, MudId, "Reinos").Title.Should().Be("Opciones del MUD Reinos");
        Model(OptionScope.Character, CharacterId, "Aldara").Title.Should().Be("Opciones del personaje Aldara");
    }

    [Fact]
    public void InheritText_NamesTheLevelAbove()
    {
        Model(OptionScope.Global).CanInherit.Should().BeFalse();
        Model(OptionScope.Global).InheritText.Should().BeEmpty();
        Model(OptionScope.Mud, MudId).InheritText.Should().Be("&Usar las opciones globales");
        Model(OptionScope.Character, CharacterId).InheritText.Should().Be("&Usar las opciones del MUD");
    }

    // ───────────────────────────── Loading ─────────────────────────────

    [Fact]
    public void Global_WithNothingStored_ShowsTheDefaults()
    {
        var model = Loaded(OptionScope.Global);

        model.IsLoaded.Should().BeTrue();
        model.UseInherited.Should().BeFalse("global has nothing to inherit from");
        model.Fields.Should().Be(OptionsFields.From(OmnimudOptions.Default));
    }

    [Fact]
    public void Global_ShowsWhatIsStored()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { HistorySize = 77, Language = "en" });

        var model = Loaded(OptionScope.Global);

        model.Fields.HistorySize.Should().Be(77);
        model.Fields.Language.Should().Be("en");
    }

    [Fact]
    public void Mud_WithoutOwnOptions_InheritsAndShowsTheGlobalOnes()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 });

        var model = Loaded(OptionScope.Mud, MudId);

        model.UseInherited.Should().BeTrue();
        model.Fields.Volume.Should().Be(40);
        model.InheritedFields.Volume.Should().Be(40);
    }

    [Fact]
    public void Mud_WithOwnOptions_ShowsItsOwn_AndKnowsTheInheritedOnes()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 })
                .With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });

        var model = Loaded(OptionScope.Mud, MudId);

        model.UseInherited.Should().BeFalse();
        model.Fields.Volume.Should().Be(90);
        model.InheritedFields.Volume.Should().Be(40);
    }

    [Fact]
    public void Character_WithoutOwnOptions_InheritsFromItsMud()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 })
                .With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });

        var model = Loaded(OptionScope.Character, CharacterId, "Aldara", MudId);

        model.UseInherited.Should().BeTrue();
        model.Fields.Volume.Should().Be(90);
    }

    [Fact]
    public void Character_WhoseMudInherits_InheritsTheGlobalOnes()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 });

        Loaded(OptionScope.Character, CharacterId, "Aldara", MudId).Fields.Volume.Should().Be(40);
    }

    [Fact]
    public void Character_WithOwnOptions_ShowsItsOwn()
    {
        _service.With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 })
                .With(OptionScope.Character, CharacterId, OmnimudOptions.Default with { Volume = 15 });

        var model = Loaded(OptionScope.Character, CharacterId, "Aldara", MudId);

        model.UseInherited.Should().BeFalse();
        model.Fields.Volume.Should().Be(15);
        model.InheritedFields.Volume.Should().Be(90);
    }

    [Fact]
    public void Character_WithoutKnownMud_FallsBackToTheGlobalOnes()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 })
                .With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });

        Loaded(OptionScope.Character, CharacterId, "Aldara").Fields.Volume.Should().Be(40);
    }

    [Fact]
    public void Language_IsOnlyEditableInTheGlobalScope()
    {
        Model(OptionScope.Global).CanEditLanguage.Should().BeTrue();
        Model(OptionScope.Mud, MudId).CanEditLanguage.Should().BeFalse();
        Model(OptionScope.Character, CharacterId).CanEditLanguage.Should().BeFalse();
    }

    // ───────────────────────────── Inherit box ─────────────────────────────

    [Fact]
    public void CheckingInherit_ShowsTheInheritedBlock_AndUncheckingBringsTheEditsBack()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 })
                .With(OptionScope.Mud, MudId, OmnimudOptions.Default with { Volume = 90 });
        var model = Loaded(OptionScope.Mud, MudId);
        var edited = model.Fields with { Volume = 55 };

        var shown = model.SetUseInherited(true, edited);
        shown.Volume.Should().Be(40);
        model.UseInherited.Should().BeTrue();

        var back = model.SetUseInherited(false, shown);
        back.Volume.Should().Be(55, "what the user had typed is not lost by trying the box");
        model.UseInherited.Should().BeFalse();
    }

    [Fact]
    public void UncheckingInherit_OnAScopeThatInherited_StartsFromACopyOfTheInheritedBlock()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 });
        var model = Loaded(OptionScope.Mud, MudId);

        var shown = model.SetUseInherited(false, model.Fields);

        shown.Should().Be(model.InheritedFields);
        shown.Should().NotBeSameAs(model.InheritedFields);
        model.UseInherited.Should().BeFalse();
    }

    [Fact]
    public void Global_CannotBeSetToInherit()
    {
        var model = Loaded(OptionScope.Global);

        model.SetUseInherited(true, model.Fields);

        model.UseInherited.Should().BeFalse();
    }

    // ───────────────────────────── Accept ─────────────────────────────

    [Fact]
    public async Task Accept_StoresTheCompleteBlock_AndTheScopeStopsInheriting()
    {
        var sample = OptionsSamples.AllNonDefault();
        var model = Loaded(OptionScope.Mud, MudId);
        model.SetUseInherited(false, model.Fields);

        var result = await model.AcceptAsync(OptionsFields.From(sample), _prompts);

        result.Saved.Should().BeTrue();
        _service.Saved.Should().ContainSingle();
        _service.Saved[0].Scope.Should().Be(OptionScope.Mud);
        _service.Saved[0].ScopeId.Should().Be(MudId);
        _service.Saved[0].Options.Should().Be(sample);
        (await _service.HasOwnOptionsAsync(OptionScope.Mud, MudId)).Should().BeTrue();
        _service.Resets.Should().BeEmpty();
    }

    [Fact]
    public async Task Accept_WhileInheriting_ResetsTheScope_WithoutValidatingOrSaving()
    {
        _service.With(OptionScope.Character, CharacterId, OmnimudOptions.Default with { Volume = 15 });
        var model = Loaded(OptionScope.Character, CharacterId, "Aldara", MudId);
        model.SetUseInherited(true, model.Fields);

        var result = await model.AcceptAsync(ValidFields() with { ProxyType = ProxyMode.Manual, ProxyHost = "" }, _prompts);

        result.Saved.Should().BeTrue();
        _service.Resets.Should().Equal((OptionScope.Character, (int?)CharacterId));
        _service.Saved.Should().BeEmpty();
        (await _service.HasOwnOptionsAsync(OptionScope.Character, CharacterId)).Should().BeFalse();
        _prompts.DidNotReceiveWithAnyArgs().Warn(default!, default);
    }

    [Fact]
    public async Task Accept_WithAnError_Warns_SavesNothing_AndSaysWhichFieldToFocus()
    {
        var model = Loaded(OptionScope.Global);

        var result = await model.AcceptAsync(ValidFields() with { ProxyType = ProxyMode.Manual, ProxyHost = " " }, _prompts);

        result.Saved.Should().BeFalse();
        result.FocusField.Should().Be(OptionsField.ProxyHost);
        _prompts.Received(1).Warn("Debes introducir el servidor proxy.", "Opciones globales");
        _service.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Accept_TrimsTextAndStoresEmptyAsNull()
    {
        var model = Loaded(OptionScope.Global);

        await model.AcceptAsync(ValidFields() with { LogDirectory = "   ", ProxyHost = "  ", FontFamily = " Arial " }, _prompts);

        var saved = _service.Saved.Single().Options;
        saved.LogDirectory.Should().BeNull();
        saved.ProxyHost.Should().BeNull();
        saved.FontFamily.Should().Be("Arial");
    }

    [Fact]
    public async Task Accept_KeepsTheStoredCharacter_WhenItsBoxIsSwitchedOffAndEmpty()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { ConcatChar = '|', RepeatChar = '*' });
        var model = Loaded(OptionScope.Global);

        var result = await model.AcceptAsync(model.Fields with { UseConcatChar = false, ConcatChar = "", UseRepeatChar = false, RepeatChar = "ab" }, _prompts);

        result.Saved.Should().BeTrue();
        _service.Saved.Single().Options.ConcatChar.Should().Be('|');
        _service.Saved.Single().Options.RepeatChar.Should().Be('*');
    }

    // ───────────────────────────── Language notice ─────────────────────────────

    [Fact]
    public async Task ChangingTheLanguage_SaysItNeedsARestart()
    {
        var model = Loaded(OptionScope.Global);

        await model.AcceptAsync(model.Fields with { Language = "en" }, _prompts);

        _prompts.Received(1).Info("El idioma cambiará la próxima vez que inicies Omnimud.", Arg.Any<string?>());
    }

    [Fact]
    public async Task KeepingTheLanguage_SaysNothing()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Language = "es" });
        var model = Loaded(OptionScope.Global);

        await model.AcceptAsync(model.Fields with { Volume = 10 }, _prompts);

        _prompts.DidNotReceiveWithAnyArgs().Info(default!, default);
    }

    [Fact]
    public async Task InAMud_TheLanguageIsNotAnnounced()
    {
        var model = Loaded(OptionScope.Mud, MudId);
        model.SetUseInherited(false, model.Fields);

        await model.AcceptAsync(model.Fields with { Language = "en" }, _prompts);

        _prompts.DidNotReceiveWithAnyArgs().Info(default!, default);
    }

    // ───────────────────────────── Log folder ─────────────────────────────

    [Fact]
    public async Task MissingLogFolder_IsCreatedWhenTheUserAgrees()
    {
        _directories.Exists(@"C:\logs\mud").Returns(false);
        _prompts.Confirm(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);
        var model = Loaded(OptionScope.Global);

        var result = await model.AcceptAsync(model.Fields with { LogType = LogMode.PerDay, LogDirectory = @" C:\logs\mud " }, _prompts);

        result.Saved.Should().BeTrue();
        _prompts.Received(1).Confirm("La carpeta de logs no existe. ¿Quieres crearla?", Arg.Any<string?>());
        _directories.Received(1).Create(@"C:\logs\mud");
        _service.Saved.Single().Options.LogDirectory.Should().Be(@"C:\logs\mud");
    }

    [Fact]
    public async Task MissingLogFolder_Refused_KeepsTheDialogOpenOnThatField()
    {
        _directories.Exists(Arg.Any<string>()).Returns(false);
        _prompts.Confirm(Arg.Any<string>(), Arg.Any<string?>()).Returns(false);
        var model = Loaded(OptionScope.Global);

        var result = await model.AcceptAsync(model.Fields with { LogDirectory = @"C:\logs\mud" }, _prompts);

        result.Should().Be(OptionsAcceptResult.Stay(OptionsField.LogDirectory));
        _directories.DidNotReceiveWithAnyArgs().Create(default!);
        _service.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task LogFolderThatCannotBeCreated_IsReported_AndNothingIsSaved()
    {
        _directories.Exists(Arg.Any<string>()).Returns(false);
        _directories.When(d => d.Create(Arg.Any<string>())).Do(_ => throw new UnauthorizedAccessException("acceso denegado"));
        _prompts.Confirm(Arg.Any<string>(), Arg.Any<string?>()).Returns(true);
        var model = Loaded(OptionScope.Global);

        var result = await model.AcceptAsync(model.Fields with { LogDirectory = @"C:\logs\mud" }, _prompts);

        result.FocusField.Should().Be(OptionsField.LogDirectory);
        _prompts.Received(1).Error("No se ha podido crear la carpeta: acceso denegado", Arg.Any<string?>());
        _service.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task LogFolder_IsNotCheckedWhenLogsAreOff_OrTheBoxIsEmpty()
    {
        _directories.Exists(Arg.Any<string>()).Returns(false);
        var model = Loaded(OptionScope.Global);

        (await model.AcceptAsync(model.Fields with { LogType = LogMode.None, LogDirectory = @"C:\nowhere" }, _prompts)).Saved.Should().BeTrue();
        (await model.AcceptAsync(model.Fields with { LogType = LogMode.PerDay, LogDirectory = "" }, _prompts)).Saved.Should().BeTrue();

        _prompts.DidNotReceiveWithAnyArgs().Confirm(default!, default);
    }

    // ───────────────────────────── Validation, one rule at a time ─────────────────────────────

    public static TheoryData<string, OptionsField, string> InvalidCases() => new()
    {
        { "history-low", OptionsField.HistorySize, "El tamaño del historial debe estar entre 1 y 10000." },
        { "history-high", OptionsField.HistorySize, "El tamaño del historial debe estar entre 1 y 10000." },
        { "lines-low", OptionsField.MaxLines, "El número máximo de líneas debe estar entre 100 y 1000000." },
        { "lines-high", OptionsField.MaxLines, "El número máximo de líneas debe estar entre 100 y 1000000." },
        { "prompt-low", OptionsField.PromptFlushMilliseconds, "La espera del prompt debe estar entre 0 y 60000 milisegundos." },
        { "prompt-high", OptionsField.PromptFlushMilliseconds, "La espera del prompt debe estar entre 0 y 60000 milisegundos." },
        { "log-path", OptionsField.LogDirectory, "La carpeta de logs no es una ruta válida." },
        { "volume-low", OptionsField.Volume, "El volumen debe estar entre 0 y 100." },
        { "volume-high", OptionsField.Volume, "El volumen debe estar entre 0 y 100." },
        { "proxy-host", OptionsField.ProxyHost, "Debes introducir el servidor proxy." },
        { "proxy-port-zero", OptionsField.ProxyPort, "El puerto del proxy debe estar entre 1 y 65535." },
        { "proxy-port-high", OptionsField.ProxyPort, "El puerto del proxy debe estar entre 1 y 65535." },
        { "proxy-port-negative-unused", OptionsField.ProxyPort, "El puerto del proxy debe estar entre 1 y 65535." },
        { "font-family", OptionsField.FontFamily, "Debes elegir una fuente." },
        { "font-small", OptionsField.FontSize, "El tamaño de la fuente debe estar entre 4 y 200 puntos." },
        { "font-big", OptionsField.FontSize, "El tamaño de la fuente debe estar entre 4 y 200 puntos." },
        { "concat-empty", OptionsField.ConcatChar, "Debes introducir el carácter que separa los comandos: exactamente un carácter." },
        { "concat-two", OptionsField.ConcatChar, "Debes introducir el carácter que separa los comandos: exactamente un carácter." },
        { "concat-digit", OptionsField.ConcatChar, "El carácter que separa los comandos no puede ser un dígito ni un espacio." },
        { "concat-space", OptionsField.ConcatChar, "El carácter que separa los comandos no puede ser un dígito ni un espacio." },
        { "repeat-empty", OptionsField.RepeatChar, "Debes introducir el carácter de repetición: exactamente un carácter." },
        { "repeat-two", OptionsField.RepeatChar, "Debes introducir el carácter de repetición: exactamente un carácter." },
        { "repeat-digit", OptionsField.RepeatChar, "El carácter de repetición no puede ser un dígito ni un espacio." },
        { "repeat-space", OptionsField.RepeatChar, "El carácter de repetición no puede ser un dígito ni un espacio." },
        { "same-chars", OptionsField.RepeatChar, "El carácter que separa los comandos y el de repetición deben ser distintos." },
    };

    private static OptionsFields Invalid(string name)
    {
        var f = ValidFields();
        return name switch
        {
            "history-low" => f with { HistorySize = 0 },
            "history-high" => f with { HistorySize = 10_001 },
            "lines-low" => f with { MaxLines = 99 },
            "lines-high" => f with { MaxLines = 1_000_001 },
            "prompt-low" => f with { PromptFlushMilliseconds = -1 },
            "prompt-high" => f with { PromptFlushMilliseconds = 60_001 },
            "log-path" => f with { LogType = LogMode.PerDay, LogDirectory = "C:\\logs\0mud" },
            "volume-low" => f with { Volume = -1 },
            "volume-high" => f with { Volume = 101 },
            "proxy-host" => f with { ProxyType = ProxyMode.Manual, ProxyHost = "  ", ProxyPort = 1080 },
            "proxy-port-zero" => f with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 0 },
            "proxy-port-high" => f with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 65_536 },
            "proxy-port-negative-unused" => f with { ProxyType = ProxyMode.Disabled, ProxyPort = -5 },
            "font-family" => f with { FontFamily = " " },
            "font-small" => f with { FontSize = 3.9f },
            "font-big" => f with { FontSize = 200.5f },
            "concat-empty" => f with { UseConcatChar = true, ConcatChar = "" },
            "concat-two" => f with { UseConcatChar = true, ConcatChar = ";;" },
            "concat-digit" => f with { UseConcatChar = true, ConcatChar = "5" },
            "concat-space" => f with { UseConcatChar = true, ConcatChar = " " },
            "repeat-empty" => f with { UseRepeatChar = true, RepeatChar = "" },
            "repeat-two" => f with { UseRepeatChar = true, RepeatChar = "##" },
            "repeat-digit" => f with { UseRepeatChar = true, RepeatChar = "3" },
            "repeat-space" => f with { UseRepeatChar = true, RepeatChar = "\t" },
            "same-chars" => f with { UseConcatChar = true, ConcatChar = "#", UseRepeatChar = true, RepeatChar = "#" },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Validate_RejectsEachProblem_WithItsMessageAndField(string name, OptionsField field, string message)
    {
        var error = Model(OptionScope.Global).Validate(Invalid(name));

        error.Should().NotBeNull();
        error!.Field.Should().Be(field);
        error.Message.Should().Be(message);
    }

    [Fact]
    public void Validate_AcceptsTheDefaults_AndTheFullSample()
    {
        var model = Model(OptionScope.Global);

        model.Validate(ValidFields()).Should().BeNull();
        model.Validate(OptionsFields.From(OptionsSamples.AllNonDefault())).Should().BeNull();
    }

    [Fact]
    public void Validate_IgnoresWhatIsSwitchedOff()
    {
        var model = Model(OptionScope.Global);

        model.Validate(ValidFields() with { ProxyType = ProxyMode.Automatic, ProxyHost = "", ProxyPort = 0 }).Should().BeNull();
        model.Validate(ValidFields() with { UseConcatChar = false, ConcatChar = "", UseRepeatChar = false, RepeatChar = "12" }).Should().BeNull();
        model.Validate(ValidFields() with { UseConcatChar = true, ConcatChar = "#", UseRepeatChar = false, RepeatChar = "#" }).Should().BeNull("only one of them is in use");
        model.Validate(ValidFields() with { LogType = LogMode.None, LogDirectory = "C:\\logs\0mud" }).Should().BeNull();
    }

    [Fact]
    public void Validate_AcceptsTheLimits()
    {
        var model = Model(OptionScope.Global);

        model.Validate(ValidFields() with { HistorySize = 1, MaxLines = 100, PromptFlushMilliseconds = 0, Volume = 0, FontSize = 4f }).Should().BeNull();
        model.Validate(ValidFields() with { HistorySize = 10_000, MaxLines = 1_000_000, PromptFlushMilliseconds = 60_000, Volume = 100, FontSize = 200f,
            ProxyType = ProxyMode.Manual, ProxyHost = "p", ProxyPort = 65_535 }).Should().BeNull();
    }

    [Fact]
    public void Messages_AreLocalized()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");

        Model(OptionScope.Global).Validate(Invalid("same-chars"))!.Message
            .Should().Be("The command stacking character and the repeat character must be different.");
        Model(OptionScope.Character, CharacterId, "Aldara").Title.Should().Be("Options of the character Aldara");
    }

    // ───────────────────────────── Conversion: every option, by reflection ─────────────────────────────

    public static TheoryData<string> OptionNames()
    {
        var data = new TheoryData<string>();
        foreach (var property in OptionsSamples.Properties) data.Add(property.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void EveryOption_SurvivesTheRoundTripThroughTheFields(string name)
    {
        var options = OptionsSamples.WithOnly(typeof(OmnimudOptions).GetProperty(name)!);
        options.Should().NotBe(OmnimudOptions.Default, "the sample must really change the option");

        OptionsFields.From(options).ToOptions().Should().Be(options,
            $"OptionsFields must carry {name}: add it to OptionsFields, to From/ToOptions and to the dialog");
    }

    [Fact]
    public void AllOptionsAtOnce_SurviveTheRoundTrip()
    {
        var options = OptionsSamples.AllNonDefault();

        OptionsFields.From(options).ToOptions().Should().Be(options);
    }

    [Theory]
    [MemberData(nameof(OptionNames))]
    public void EveryOption_HasAFieldAndAnEntryInTheFieldEnum(string name)
    {
        typeof(OptionsFields).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            .Should().NotBeNull($"the dialog must edit {name}");
        Enum.GetNames<OptionsField>().Should().Contain(name, $"validation errors must be able to point at {name}");
    }

    [Fact]
    public void DependentFlags_FollowTheirMasters()
    {
        var f = ValidFields();

        (f with { LogType = LogMode.None }).LogDirectoryEnabled.Should().BeFalse();
        (f with { LogType = LogMode.PerSession }).LogDirectoryEnabled.Should().BeTrue();
        (f with { EnableSounds = false }).SoundsBackgroundEnabled.Should().BeFalse();
        (f with { EnableMusic = false }).MusicBackgroundEnabled.Should().BeFalse();
        (f with { DownloadSounds = false }).AllowHttpEnabled.Should().BeFalse();
        (f with { ProxyType = ProxyMode.Disabled }).ProxyDetailsEnabled.Should().BeFalse();
        (f with { ProxyType = ProxyMode.Automatic }).ProxyDetailsEnabled.Should().BeFalse();
        (f with { ProxyType = ProxyMode.Manual }).ProxyDetailsEnabled.Should().BeTrue();
        (f with { ProxyType = ProxyMode.Disabled }).ProxyForMudEnabled.Should().BeFalse();
        (f with { ProxyType = ProxyMode.Automatic }).ProxyForMudEnabled.Should().BeTrue();
        (f with { UseConcatChar = true }).ConcatCharEnabled.Should().BeTrue();
        (f with { UseRepeatChar = false }).RepeatCharEnabled.Should().BeFalse();
    }

    // ───────────────────────────── Export / import ─────────────────────────────

    [Fact]
    public async Task Export_WritesWhatIsOnScreen()
    {
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns(@"C:\tmp\mis.omnimud");
        var model = Loaded(OptionScope.Global);
        var sample = OptionsSamples.AllNonDefault();

        var focus = await model.ExportAsync(OptionsFields.From(sample), _prompts);

        focus.Should().BeNull();
        await _files.Received(1).SaveAsync(@"C:\tmp\mis.omnimud", sample with { ProxyPasswordProtected = null }, Arg.Any<CancellationToken>());
        _prompts.Received(1).PickSaveFile("Exportar opciones", Arg.Is<string>(f => f.Contains("*.omnimud")), "opciones.omnimud");
        _prompts.Received(1).Info("Opciones exportadas.", Arg.Any<string?>());
        _service.Saved.Should().BeEmpty("exporting does not store anything");
    }

    [Fact]
    public async Task Export_WhileInheriting_WritesTheInheritedBlock()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Volume = 40 });
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("x.omnimud");
        var model = Loaded(OptionScope.Mud, MudId);

        await model.ExportAsync(model.Fields with { Volume = 1, ProxyType = ProxyMode.Manual }, _prompts);

        await _files.Received(1).SaveAsync("x.omnimud", Arg.Is<OmnimudOptions>(o => o.Volume == 40 && o.ProxyType == ProxyMode.Disabled), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_OfInvalidFields_WarnsAndPointsAtTheField()
    {
        var model = Loaded(OptionScope.Global);

        var focus = await model.ExportAsync(Invalid("font-family"), _prompts);

        focus.Should().Be(OptionsField.FontFamily);
        _prompts.Received(1).Warn("Debes elegir una fuente.", Arg.Any<string?>());
        _prompts.DidNotReceiveWithAnyArgs().PickSaveFile(default!, default!, default);
    }

    [Fact]
    public async Task Export_Cancelled_DoesNothing()
    {
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns((string?)null);
        var model = Loaded(OptionScope.Global);

        await model.ExportAsync(model.Fields, _prompts);

        await _files.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default);
    }

    [Fact]
    public async Task Export_Failure_IsReported()
    {
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("x.omnimud");
        _files.SaveAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromException(new OptionsFileException("disco lleno")));
        var model = Loaded(OptionScope.Global);

        await model.ExportAsync(model.Fields, _prompts);

        _prompts.Received(1).Error("No se han podido exportar las opciones: disco lleno", Arg.Any<string?>());
    }

    [Fact]
    public async Task Import_FillsTheDialog_StopsInheriting_AndStoresNothing()
    {
        var sample = OptionsSamples.AllNonDefault();
        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("in.omnimud");
        _files.LoadAsync("in.omnimud", Arg.Any<CancellationToken>()).Returns(sample);
        var model = Loaded(OptionScope.Global);

        var fields = await model.ImportAsync(model.Fields, _prompts);

        fields.Should().Be(OptionsFields.From(sample with { ProxyPasswordProtected = null, CheckUpdatesOnStartup = false }),
            "a file never brings a proxy password, and never switches on the update check: the dialog keeps its own (none, and off, here)");
        model.Fields.Should().Be(fields);
        _service.Saved.Should().BeEmpty("nothing is stored until OK");
        _prompts.Received(1).Info("Opciones cargadas. Se guardarán cuando pulses Aceptar.", Arg.Any<string?>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_NeverChangesTheUpdateCheck_WhateverTheFileSays(bool current)
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { CheckUpdatesOnStartup = current });
        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("in.omnimud");
        _files.LoadAsync("in.omnimud", Arg.Any<CancellationToken>()).Returns(OmnimudOptions.Default with { CheckUpdatesOnStartup = !current, Volume = 12 });
        var model = Loaded(OptionScope.Global);

        var fields = await model.ImportAsync(model.Fields, _prompts);

        fields!.Volume.Should().Be(12);
        fields.CheckUpdatesOnStartup.Should().Be(current, "going to the network on startup is only ever switched on by the user ticking the box");
    }

    [Fact]
    public void TheUpdateCheck_IsAnApplicationOption_OnlyEditableInGlobal()
    {
        Model(OptionScope.Global).CanEditApplicationOptions.Should().BeTrue();
        Model(OptionScope.Mud, MudId).CanEditApplicationOptions.Should().BeFalse();
        Model(OptionScope.Character, CharacterId).CanEditApplicationOptions.Should().BeFalse();
    }

    [Fact]
    public async Task Import_IntoAnInheritingMud_UnchecksInherit_AndKeepsTheLanguage()
    {
        _service.With(OptionScope.Global, null, OmnimudOptions.Default with { Language = "en" });
        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("in.omnimud");
        _files.LoadAsync("in.omnimud", Arg.Any<CancellationToken>()).Returns(OmnimudOptions.Default with { Language = "es", Volume = 12 });
        var model = Loaded(OptionScope.Mud, MudId);

        var fields = await model.ImportAsync(model.Fields, _prompts);

        model.UseInherited.Should().BeFalse();
        fields!.Volume.Should().Be(12);
        fields.Language.Should().Be("en", "the language is not editable in a MUD");
    }

    [Fact]
    public async Task Import_Cancelled_OrFailed_ChangesNothing()
    {
        var model = Loaded(OptionScope.Mud, MudId);

        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        (await model.ImportAsync(model.Fields, _prompts)).Should().BeNull();

        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("bad.omnimud");
        _files.LoadAsync("bad.omnimud", Arg.Any<CancellationToken>()).Returns(Task.FromException<OmnimudOptions>(new OptionsFileException("El fichero no contiene opciones.")));
        (await model.ImportAsync(model.Fields, _prompts)).Should().BeNull();

        model.UseInherited.Should().BeTrue();
        _prompts.Received(1).Error("No se han podido importar las opciones: El fichero no contiene opciones.", Arg.Any<string?>());
    }
}
