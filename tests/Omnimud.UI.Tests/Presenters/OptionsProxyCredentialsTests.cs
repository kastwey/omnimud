using System.Globalization;
using System.Security.Cryptography;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Security;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;
using Omnimud.UI.Services;

namespace Omnimud.UI.Tests.Presenters;

/// <summary>User name and password of the proxy in the options dialog's logic, without a window.</summary>
public sealed class OptionsProxyCredentialsTests
{
    private const string Typed = "s3cret-tecleada";

    private readonly FakeOptionsService _service = new();
    private readonly IUserPrompts _prompts = Substitute.For<IUserPrompts>();
    private readonly IOptionsFileStore _files = Substitute.For<IOptionsFileStore>();
    private readonly IDirectoryAccess _directories = Substitute.For<IDirectoryAccess>();
    private readonly ProxyCredentialStore _credentials = new(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));

    public OptionsProxyCredentialsTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("es");
        _directories.Exists(Arg.Any<string>()).Returns(true);
    }

    private OptionsEditorModel Loaded(bool withStore = true, OptionScope scope = OptionScope.Global, int? id = null)
    {
        var model = new OptionsEditorModel(_service, scope, id, "x", null, _files, _directories, withStore ? _credentials : null);
        model.LoadAsync().GetAwaiter().GetResult();
        return model;
    }

    private OmnimudOptions Stored(string? password = "la-guardada") =>
        _credentials.WithPassword(OmnimudOptions.Default with
        {
            ProxyType = ProxyMode.Manual, ProxyHost = "proxy.corp", ProxyPort = 3128,
            ProxyProtocol = ProxyProtocol.HttpConnect, ProxyUsername = "juan"
        }, password);

    // ───────────────────────────── What the dialog shows ─────────────────────────────

    [Fact]
    public void Loading_NeverPutsThePasswordInTheFields_OnlyTheOpaqueStoredValue()
    {
        var stored = Stored();
        _service.With(OptionScope.Global, null, stored);

        var fields = Loaded().Fields;

        fields.ProxyUsername.Should().Be("juan");
        fields.NewProxyPassword.Should().BeEmpty("the password box always starts empty");
        fields.ClearProxyPassword.Should().BeFalse();
        fields.ProxyPasswordProtected.Should().Be(stored.ProxyPasswordProtected);
        fields.HasStoredProxyPassword.Should().BeTrue();
    }

    [Fact]
    public void Fields_ToString_ShowsNeitherTheTypedPasswordNorTheStoredOne()
    {
        var fields = OptionsFields.From(Stored()) with { NewProxyPassword = Typed };

        var text = fields.ToString();

        text.Should().Contain("ProxyUsername = juan").And.Contain("NewProxyPassword = ***").And.Contain("ProxyPasswordProtected = ***");
        text.Should().NotContain(Typed).And.NotContain(fields.ProxyPasswordProtected!);
    }

    [Theory]
    [InlineData(ProxyMode.Disabled, false, false)]
    [InlineData(ProxyMode.Automatic, true, true)]
    [InlineData(ProxyMode.Manual, true, true)]
    public void Credentials_AreForManualAndAutomatic_AndDeleteOnlyWhenThereIsSomethingToDelete(ProxyMode mode, bool credentials, bool clear)
    {
        var withPassword = OptionsFields.From(Stored()) with { ProxyType = mode };
        var without = OptionsFields.From(Stored(null)) with { ProxyType = mode };

        withPassword.ProxyCredentialsEnabled.Should().Be(credentials);
        withPassword.ClearProxyPasswordEnabled.Should().Be(clear);
        without.ProxyCredentialsEnabled.Should().Be(credentials);
        without.ClearProxyPasswordEnabled.Should().BeFalse();
    }

    // ───────────────────────────── Accept ─────────────────────────────

    [Fact]
    public async Task EmptyPasswordBox_KeepsTheStoredPassword()
    {
        var stored = Stored();
        _service.With(OptionScope.Global, null, stored);
        var model = Loaded();

        var result = await model.AcceptAsync(model.Fields with { ProxyUsername = "otro", Volume = 7 }, _prompts);

        result.Saved.Should().BeTrue();
        var saved = _service.Saved.Single().Options;
        saved.Should().Be(stored with { ProxyUsername = "otro", Volume = 7 });
        _credentials.GetPassword(saved).Should().Be("la-guardada");
    }

    [Fact]
    public async Task TypedPassword_IsStoredProtected_NeverInClear()
    {
        _service.With(OptionScope.Global, null, Stored());
        var model = Loaded();

        var result = await model.AcceptAsync(model.Fields with { NewProxyPassword = Typed }, _prompts);

        result.Saved.Should().BeTrue();
        var saved = _service.Saved.Single().Options;
        saved.ProxyPasswordProtected.Should().NotBeNullOrEmpty().And.NotContain(Typed);
        OptionsSerializer.Serialize(saved).Values.Should().NotContain(v => v.Contains(Typed), "what reaches the database has no clear password");
        _credentials.GetPassword(saved).Should().Be(Typed);
        model.Fields.NewProxyPassword.Should().BeEmpty("after saving, the model holds no clear password either");
        model.Fields.ProxyPasswordProtected.Should().Be(saved.ProxyPasswordProtected);
    }

    [Fact]
    public async Task TypedPassword_ForTheFirstTime_InAutomaticMode_IsStoredToo()
    {
        var model = Loaded();

        await model.AcceptAsync(model.Fields with { ProxyType = ProxyMode.Automatic, ProxyUsername = "juan", NewProxyPassword = Typed }, _prompts);

        _credentials.GetPassword(_service.Saved.Single().Options).Should().Be(Typed);
    }

    [Fact]
    public async Task DeleteBox_RemovesTheStoredPassword()
    {
        _service.With(OptionScope.Global, null, Stored());
        var model = Loaded();

        await model.AcceptAsync(model.Fields with { ClearProxyPassword = true }, _prompts);

        var saved = _service.Saved.Single().Options;
        saved.ProxyPasswordProtected.Should().BeNull();
        saved.ProxyUsername.Should().Be("juan", "only the password is deleted");
    }

    [Fact]
    public async Task DeleteBox_AndANewPassword_TheNewPasswordWins()
    {
        _service.With(OptionScope.Global, null, Stored());
        var model = Loaded();

        await model.AcceptAsync(model.Fields with { ClearProxyPassword = true, NewProxyPassword = Typed }, _prompts);

        _credentials.GetPassword(_service.Saved.Single().Options).Should().Be(Typed);
    }

    [Fact]
    public async Task WithTheProxyDisabled_ATypedPasswordIsNotStored_AndTheOldOneStays()
    {
        var stored = Stored();
        _service.With(OptionScope.Global, null, stored);
        var model = Loaded();

        await model.AcceptAsync(model.Fields with { ProxyType = ProxyMode.Disabled, NewProxyPassword = Typed }, _prompts);

        _service.Saved.Single().Options.ProxyPasswordProtected.Should().Be(stored.ProxyPasswordProtected);
    }

    [Fact]
    public async Task APasswordProtectedOnAnotherComputer_CanBeReplaced_OrDeleted()
    {
        var foreign = new ProxyCredentialStore(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));
        _service.With(OptionScope.Global, null, foreign.WithPassword(Stored(null), "de-otro-equipo"));
        var model = Loaded();
        model.Fields.ClearProxyPasswordEnabled.Should().BeTrue();

        await model.AcceptAsync(model.Fields with { NewProxyPassword = Typed }, _prompts);

        _credentials.GetPassword(_service.Saved.Single().Options).Should().Be(Typed);
    }

    // ───────────────────────────── Validation ─────────────────────────────

    public static TheoryData<string, OptionsField, string> InvalidCases => new()
    {
        { "password-without-user", OptionsField.ProxyUsername, "Introduce el usuario del proxy o deja la contraseña vacía." },
        { "password-blank-user", OptionsField.ProxyUsername, "Introduce el usuario del proxy o deja la contraseña vacía." },
        { "http-user-colon", OptionsField.ProxyUsername, "El usuario de un proxy HTTP no puede contener dos puntos." },
        { "automatic-user-colon", OptionsField.ProxyUsername, "El usuario de un proxy HTTP no puede contener dos puntos." },
        { "user-too-long", OptionsField.ProxyUsername, "El usuario del proxy es demasiado largo (255 bytes como máximo)." },
        { "password-too-long", OptionsField.ProxyPasswordProtected, "La contraseña del proxy es demasiado larga (255 bytes como máximo)." },
    };

    private static OptionsFields Invalid(string name)
    {
        var f = OptionsFields.From(OmnimudOptions.Default) with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080 };
        return name switch
        {
            "password-without-user" => f with { NewProxyPassword = Typed },
            "password-blank-user" => f with { ProxyUsername = "   ", NewProxyPassword = Typed },
            "http-user-colon" => f with { ProxyProtocol = ProxyProtocol.HttpConnect, ProxyUsername = "dominio:juan" },
            "automatic-user-colon" => f with { ProxyType = ProxyMode.Automatic, ProxyUsername = "dominio:juan" },
            "user-too-long" => f with { ProxyUsername = new string('ñ', 128) },
            "password-too-long" => f with { ProxyUsername = "juan", NewProxyPassword = new string('x', 256) },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public async Task Validation_RejectsEachProblem_WithItsMessageAndField_AndNeverEchoesThePassword(string name, OptionsField field, string message)
    {
        var model = Loaded();

        var error = model.Validate(Invalid(name));
        var result = await model.AcceptAsync(Invalid(name), _prompts);

        error.Should().Be(new OptionsValidationError(field, message));
        message.Should().NotContain(Typed);
        result.Should().Be(OptionsAcceptResult.Stay(field));
        _service.Saved.Should().BeEmpty();
    }

    [Fact]
    public void Validation_AcceptsWhatIsFine()
    {
        var model = Loaded();
        var f = OptionsFields.From(OmnimudOptions.Default) with { ProxyType = ProxyMode.Manual, ProxyHost = "proxy", ProxyPort = 1080 };

        model.Validate(f with { ProxyUsername = "juan" }).Should().BeNull("a user without password is a valid setup");
        model.Validate(f with { ProxyUsername = "juan", NewProxyPassword = new string('x', 255) }).Should().BeNull();
        model.Validate(f with { ProxyProtocol = ProxyProtocol.Socks5, ProxyUsername = "dominio:juan" }).Should().BeNull("SOCKS5 has no problem with a colon");
        model.Validate(f with { ProxyType = ProxyMode.Disabled, ProxyUsername = "a:b", NewProxyPassword = new string('x', 999) })
            .Should().BeNull("nothing about the proxy is checked while it is off");
    }

    [Fact]
    public async Task WithoutACredentialStore_ANewPasswordIsRefused_ButKeepingAndDeletingWork()
    {
        var stored = Stored();
        _service.With(OptionScope.Global, null, stored);
        var model = Loaded(withStore: false);

        var refused = await model.AcceptAsync(model.Fields with { NewProxyPassword = Typed }, _prompts);
        refused.Should().Be(OptionsAcceptResult.Stay(OptionsField.ProxyPasswordProtected));
        _prompts.Received(1).Warn("Este diálogo no puede guardar contraseñas. Deja vacía la contraseña del proxy.", Arg.Any<string?>());
        _service.Saved.Should().BeEmpty("storing it in clear is not an option");

        (await model.AcceptAsync(model.Fields, _prompts)).Saved.Should().BeTrue();
        _service.Saved.Single().Options.Should().Be(stored);
    }

    // ───────────────────────────── Export / import ─────────────────────────────

    [Fact]
    public async Task Export_NeverCarriesThePassword_NotTheStoredOne_NorTheTypedOne()
    {
        _service.With(OptionScope.Global, null, Stored());
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("out.omnimud");
        var model = Loaded();

        await model.ExportAsync(model.Fields with { NewProxyPassword = Typed }, _prompts);

        await _files.Received(1).SaveAsync("out.omnimud",
            Arg.Is<OmnimudOptions>(o => o.ProxyPasswordProtected == null && o.ProxyUsername == "juan" && !o.ToString().Contains(Typed)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Export_WhileInheriting_DoesNotCarryTheInheritedPasswordEither()
    {
        _service.With(OptionScope.Global, null, Stored());
        _prompts.PickSaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("out.omnimud");
        var model = Loaded(scope: OptionScope.Mud, id: 3);
        model.UseInherited.Should().BeTrue();

        await model.ExportAsync(model.Fields, _prompts);

        await _files.Received(1).SaveAsync("out.omnimud", Arg.Is<OmnimudOptions>(o => o.ProxyPasswordProtected == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Import_KeepsThePasswordTheDialogHad_AndWhatTheUserWasDoingWithIt()
    {
        var stored = Stored();
        _service.With(OptionScope.Global, null, stored);
        _prompts.PickOpenFile(Arg.Any<string>(), Arg.Any<string>()).Returns("in.omnimud");
        _files.LoadAsync("in.omnimud", Arg.Any<CancellationToken>())
            .Returns(OmnimudOptions.Default with { Volume = 12, ProxyUsername = "del-fichero", ProxyPasswordProtected = "QUpFTkE=" });
        var model = Loaded();

        var fields = await model.ImportAsync(model.Fields with { NewProxyPassword = Typed }, _prompts);

        fields!.Volume.Should().Be(12);
        fields.ProxyUsername.Should().Be("del-fichero");
        fields.ProxyPasswordProtected.Should().Be(stored.ProxyPasswordProtected, "a file cannot bring a password");
        fields.NewProxyPassword.Should().Be(Typed);

        await model.AcceptAsync(fields with { NewProxyPassword = string.Empty }, _prompts);
        _credentials.GetPassword(_service.Saved.Single().Options).Should().Be("la-guardada");
    }
}
