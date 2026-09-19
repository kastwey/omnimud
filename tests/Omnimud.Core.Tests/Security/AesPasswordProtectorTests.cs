using FluentAssertions;
using Omnimud.Core.Security;
using System.Security.Cryptography;

namespace Omnimud.Core.Tests.Security;

public class AesPasswordProtectorTests
{
    private readonly AesPasswordProtector _sut;

    public AesPasswordProtectorTests()
    {
        var masterKey = RandomNumberGenerator.GetBytes(32);
        _sut = new AesPasswordProtector(masterKey);
    }

    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        var password = "MySecretP@ssw0rd!";

        var encrypted = _sut.Protect(password);
        var decrypted = _sut.Unprotect(encrypted);

        decrypted.Should().Be(password);
    }

    [Fact]
    public void Protect_SamePassword_DifferentCiphertext()
    {
        var password = "SamePassword";

        var encrypted1 = _sut.Protect(password);
        var encrypted2 = _sut.Protect(password);

        // Due to random salt and nonce, ciphertext should differ
        encrypted1.Should().NotBeEquivalentTo(encrypted2);
    }

    [Fact]
    public void Protect_ResultIsNotPlaintext()
    {
        var password = "ObviousPassword123";

        var encrypted = _sut.Protect(password);
        var asString = System.Text.Encoding.UTF8.GetString(encrypted);

        asString.Should().NotContain(password);
    }

    [Fact]
    public void Unprotect_TamperedData_Throws()
    {
        var password = "TestPassword";
        var encrypted = _sut.Protect(password);

        // Tamper with ciphertext (last byte)
        encrypted[^1] ^= 0xFF;

        var act = () => _sut.Unprotect(encrypted);
        act.Should().Throw<Exception>(); // AuthenticationTagMismatchException or CryptographicException
    }

    [Fact]
    public void Unprotect_TooShortData_ThrowsArgumentException()
    {
        var act = () => _sut.Unprotect(new byte[10]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WrongKeySize_ThrowsArgumentException()
    {
        var act = () => new AesPasswordProtector(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Protect_EmptyPassword_ThrowsArgumentException()
    {
        var act = () => _sut.Protect("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ProtectUnprotect_UnicodePassword_RoundTrips()
    {
        var password = "contraseña_àéîõü_日本語";

        var encrypted = _sut.Protect(password);
        var decrypted = _sut.Unprotect(encrypted);

        decrypted.Should().Be(password);
    }

    [Fact]
    public void DifferentKeys_CannotDecryptEachOther()
    {
        var key1 = RandomNumberGenerator.GetBytes(32);
        var key2 = RandomNumberGenerator.GetBytes(32);
        var protector1 = new AesPasswordProtector(key1);
        var protector2 = new AesPasswordProtector(key2);

        var encrypted = protector1.Protect("secret");

        var act = () => protector2.Unprotect(encrypted);
        act.Should().Throw<Exception>();
    }
}
