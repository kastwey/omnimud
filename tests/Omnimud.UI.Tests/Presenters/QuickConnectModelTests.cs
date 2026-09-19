using System.Globalization;
using Omnimud.Core.Session;
using Omnimud.UI.Presenters;
using Omnimud.UI.Resources;

namespace Omnimud.UI.Tests.Presenters;

public sealed class QuickConnectModelTests : IDisposable
{
    private readonly TempDatabase _db = new();

    public QuickConnectModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("es");

    public void Dispose() => _db.Dispose();

    private static QuickConnectModel Valid(IQuickConnectStore? store = null) => new(store) { Host = "mud.org", Port = "4000" };

    [Fact]
    public async Task TheFirstTime_HostAndPortAreEmpty_NoLocalhostNor23()
    {
        var model = new QuickConnectModel(new OptionQuickConnectStore(new Omnimud.Data.Repositories.SqliteOptionRepository(_db.Factory)));

        await model.LoadAsync();

        model.Should().BeEquivalentTo(new { Host = "", Port = "", UseTls = false, ValidateCertificate = true, Encoding = "utf-8" });
        model.CanValidateCertificate.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mud example")]
    public void Host_IsRequired_AndHasNoSpaces(string host)
    {
        var model = Valid();
        model.Host = host;

        model.Validate()!.Field.Should().Be(QuickConnectField.Host);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("0", false)]
    [InlineData("65536", false)]
    [InlineData("-5", false)]
    [InlineData("abc", false)]
    [InlineData("23.5", false)]
    public void Port_IsRequired_AndBetween1And65535(string port, bool missing)
    {
        var model = Valid();
        model.Port = port;

        model.Validate().Should().BeEquivalentTo(new FieldError<QuickConnectField>(QuickConnectField.Port,
            missing ? Strings.QuickConnect_PortRequired : Strings.QuickConnect_PortInvalid));
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" 65535 ")]
    public void Port_Limits_AreValid(string port)
    {
        var model = Valid();
        model.Port = port;

        model.Validate().Should().BeNull();
    }

    [Fact]
    public void Encoding_IsValidated()
    {
        var model = Valid();
        model.Encoding = "nope-1";

        var error = model.Validate();

        error!.Field.Should().Be(QuickConnectField.Encoding);
        error.Message.Should().Contain("nope-1");
    }

    [Fact]
    public async Task Accept_ReturnsTheWholeProfile()
    {
        var model = new QuickConnectModel { Host = " mud.org ", Port = "4443", UseTls = true, ValidateCertificate = false, Encoding = " windows-1252 " };

        var (profile, error) = await model.AcceptAsync();

        error.Should().BeNull();
        profile.Should().BeEquivalentTo(new SessionProfile
        {
            Title = "mud.org:4443", Host = "mud.org", Port = 4443, UseTls = true, ValidateCertificate = false, Encoding = "windows-1252",
        });
    }

    [Fact]
    public async Task Accept_Invalid_ReturnsTheError_AndRemembersNothing()
    {
        var store = new MemoryQuickConnectStore();
        var model = new QuickConnectModel(store) { Host = "mud.org", Port = "" };

        var (profile, error) = await model.AcceptAsync();

        profile.Should().BeNull();
        error!.Field.Should().Be(QuickConnectField.Port);
        (await store.LoadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task TheLastConnection_IsRemembered_InTheDatabase_AndComesBackFilled()
    {
        var options = new Omnimud.Data.Repositories.SqliteOptionRepository(_db.Factory);
        var first = new QuickConnectModel(new OptionQuickConnectStore(options))
        {
            Host = "mud.org", Port = "4443", UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1",
        };
        (await first.AcceptAsync()).Error.Should().BeNull();

        var next = new QuickConnectModel(new OptionQuickConnectStore(options));
        await next.LoadAsync();

        next.Should().BeEquivalentTo(new { Host = "mud.org", Port = "4443", UseTls = true, ValidateCertificate = false, Encoding = "iso-8859-1" });
        next.Validate().Should().BeNull();
    }

    [Fact]
    public async Task TheMemory_LivesInUiKeys_OutsideTheGlobalOptions()
    {
        var options = new Omnimud.Data.Repositories.SqliteOptionRepository(_db.Factory);
        await Valid(new OptionQuickConnectStore(options)).AcceptAsync();

        (await options.HasAnyAsync(0, null)).Should().BeFalse("saving the global options rewrites scope 0 and would wipe interface state");
        var rows = await options.GetByScope(OptionQuickConnectStore.UiStateScope, null);
        rows.Select(r => r.Key).Should().BeEquivalentTo(
            "Ui.QuickConnect.Host", "Ui.QuickConnect.Port", "Ui.QuickConnect.UseTls", "Ui.QuickConnect.ValidateCertificate", "Ui.QuickConnect.Encoding");
    }

    [Fact]
    public async Task OnlyTheLastOne_IsKept()
    {
        var store = new MemoryQuickConnectStore();
        await new QuickConnectModel(store) { Host = "uno.org", Port = "1" }.AcceptAsync();
        await new QuickConnectModel(store) { Host = "dos.org", Port = "2" }.AcceptAsync();

        (await store.LoadAsync()).Should().Be(new QuickConnectSettings("dos.org", 2, false, true, "utf-8"));
    }

    [Fact]
    public async Task DamagedStoredValues_FallBackToSafeOnes()
    {
        var options = new Omnimud.Data.Repositories.SqliteOptionRepository(_db.Factory);
        await options.SetValueAsync(OptionQuickConnectStore.UiStateScope, null, "Ui.QuickConnect.Host", "mud.org");
        await options.SetValueAsync(OptionQuickConnectStore.UiStateScope, null, "Ui.QuickConnect.Port", "999999");
        await options.SetValueAsync(OptionQuickConnectStore.UiStateScope, null, "Ui.QuickConnect.Encoding", "klingon");
        var model = new QuickConnectModel(new OptionQuickConnectStore(options));

        await model.LoadAsync();

        model.Should().BeEquivalentTo(new { Host = "mud.org", Port = "", ValidateCertificate = true, Encoding = "utf-8" });
    }
}
