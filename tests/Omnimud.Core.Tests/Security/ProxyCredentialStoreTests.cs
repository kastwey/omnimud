using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NSubstitute;
using Omnimud.Core.Options;
using Omnimud.Core.Security;

namespace Omnimud.Core.Tests.Security;

public sealed class ProxyCredentialStoreTests
{
    private const string Password = "s3cret-Pässw0rd-ñ";

    private static ProxyCredentialStore NewStore() => new(new AesPasswordProtector(RandomNumberGenerator.GetBytes(32)));

    [Fact]
    public void WithPassword_ThenGetPassword_RoundTrips()
    {
        var store = NewStore();

        var options = store.WithPassword(OmnimudOptions.Default, Password);

        store.HasPassword(options).Should().BeTrue();
        store.GetPassword(options).Should().Be(Password);
    }

    [Fact]
    public void TheStoredValue_IsBase64_AndHasNothingOfThePasswordInIt()
    {
        var options = NewStore().WithPassword(OmnimudOptions.Default, Password);

        var stored = options.ProxyPasswordProtected!;
        var bytes = Convert.FromBase64String(stored);
        stored.Should().NotContain(Password);
        Encoding.UTF8.GetString(bytes).Should().NotContain("s3cret");
        Encoding.Latin1.GetString(bytes).Should().NotContain("s3cret");
        bytes.Length.Should().BeGreaterThan(Encoding.UTF8.GetByteCount(Password), "salt, nonce and tag travel with it");
    }

    [Fact]
    public void TheSamePasswordTwice_IsStoredDifferently()
    {
        var store = NewStore();

        store.WithPassword(OmnimudOptions.Default, Password).ProxyPasswordProtected
            .Should().NotBe(store.WithPassword(OmnimudOptions.Default, Password).ProxyPasswordProtected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NullOrEmptyPassword_RemovesTheStoredOne(string? nothing)
    {
        var store = NewStore();
        var options = store.WithPassword(OmnimudOptions.Default, Password);

        var cleared = store.WithPassword(options, nothing);

        cleared.ProxyPasswordProtected.Should().BeNull();
        store.HasPassword(cleared).Should().BeFalse();
        store.GetPassword(cleared).Should().BeNull();
    }

    [Fact]
    public void WithPassword_TouchesNothingElse()
    {
        var before = OmnimudOptions.Default with { ProxyUsername = "juan", ProxyHost = "proxy", Volume = 12 };

        var after = NewStore().WithPassword(before, Password);

        (after with { ProxyPasswordProtected = null }).Should().Be(before);
    }

    [Fact]
    public void AnotherMasterKey_MeansNoPassword_WithoutThrowing()
    {
        // The data folder was copied to another computer: the master key is bound to the Windows user.
        var options = NewStore().WithPassword(OmnimudOptions.Default, Password);
        var elsewhere = NewStore();

        var read = () => elsewhere.GetPassword(options);

        read.Should().NotThrow().Which.Should().BeNull();
        elsewhere.HasPassword(options).Should().BeTrue("it is still there, so the dialog can offer to delete it");
    }

    [Theory]
    [InlineData("esto no es base64 !!")]
    [InlineData("AAAA")]
    [InlineData("    ")]
    [InlineData("c2FsdCtub25jZSt0YWcrY2lmcmFkbytiYXN0YW50ZStsYXJnbytwYXJhK3Bhc2FyK2VsK21pbmltbw==")]
    public void DamagedValue_MeansNoPassword_WithoutThrowing(string stored)
    {
        var options = OmnimudOptions.Default with { ProxyPasswordProtected = stored };

        var read = () => NewStore().GetPassword(options);

        read.Should().NotThrow().Which.Should().BeNull();
    }

    [Fact]
    public void AProtectorThatFailsInAnyExpectedWay_MeansNoPassword()
    {
        var protector = Substitute.For<IPasswordProtector>();
        protector.Unprotect(Arg.Any<byte[]>()).Returns(_ => throw new CryptographicException("bad tag"));
        var options = OmnimudOptions.Default with { ProxyPasswordProtected = Convert.ToBase64String(new byte[64]) };

        new ProxyCredentialStore(protector).GetPassword(options).Should().BeNull();
    }

    [Fact]
    public void Options_ToString_MasksTheProtectedPassword_ButKeepsTheRest()
    {
        var options = NewStore().WithPassword(OmnimudOptions.Default with { ProxyUsername = "juan", Volume = 37 }, Password);

        var text = options.ToString();

        text.Should().StartWith("OmnimudOptions {").And.Contain("ProxyUsername = juan").And.Contain("Volume = 37");
        text.Should().Contain("ProxyPasswordProtected = ***");
        text.Should().NotContain(options.ProxyPasswordProtected!).And.NotContain(Password);
        OmnimudOptions.Default.ToString().Should().Contain("ProxyPasswordProtected = ,", "nothing to mask when there is none");
    }

    [Fact]
    public void Options_Equality_StillSeesThePassword()
    {
        var store = NewStore();
        var one = store.WithPassword(OmnimudOptions.Default, Password);

        one.Should().NotBe(OmnimudOptions.Default);
        (one with { }).Should().Be(one);
    }

    [Fact]
    public void NullStore_KeepsNothing_AndReadsNothing()
    {
        var store = NullProxyCredentialStore.Instance;
        var options = store.WithPassword(OmnimudOptions.Default with { ProxyPasswordProtected = "algo" }, Password);

        options.ProxyPasswordProtected.Should().BeNull();
        store.GetPassword(OmnimudOptions.Default with { ProxyPasswordProtected = "algo" }).Should().BeNull();
        store.HasPassword(OmnimudOptions.Default with { ProxyPasswordProtected = "algo" }).Should().BeFalse();
    }
}
