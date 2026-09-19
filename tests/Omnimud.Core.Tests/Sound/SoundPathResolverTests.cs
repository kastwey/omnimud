using FluentAssertions;
using Omnimud.Core.Sound;

namespace Omnimud.Core.Tests.Sound;

public sealed class SoundPathResolverTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "omnimud_resolver_base");

    [Theory]
    [InlineData(null, "a.wav", @"a.wav")]
    [InlineData(null, "a", @"a.wav")]
    [InlineData(null, "a.MP3", @"a.MP3")]
    [InlineData(null, "a.v2", @"a.v2.wav")]
    [InlineData("zone", "a.ogg", @"zone\a.ogg")]
    [InlineData("zone/deep", "x/a.ogg", @"zone\deep\x\a.ogg")]
    [InlineData(null, @"x\\a.ogg", @"x\a.ogg")]
    [InlineData(null, "growl*", @"growl*.wav")]
    [InlineData(null, "growl?.*", @"growl?.*")]
    public void Combine_SafeNames_StayInsideTheBase(string? subfolder, string name, string expectedRelative)
    {
        SoundPathResolver.Combine(Base, subfolder, name).Should().Be(Path.Combine(Base, expectedRelative));
    }

    [Theory]
    [InlineData(null, "..")]
    [InlineData(null, @"..\a.wav")]
    [InlineData(null, "../a.wav")]
    [InlineData(null, @"x\..\..\a.wav")]
    [InlineData(null, @"x\.\a.wav")]
    [InlineData(null, @"\a.wav")]
    [InlineData(null, "/a.wav")]
    [InlineData(null, @"\\server\share\a.wav")]
    [InlineData(null, @"C:\Windows\Media\ding.wav")]
    [InlineData(null, "C:ding.wav")]
    [InlineData(null, "a.wav:stream")]
    [InlineData(null, "a<b>.wav")]
    [InlineData(null, "dir*/a.wav")]
    [InlineData(null, "a.wav. ")]
    [InlineData(null, "...")]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    [InlineData("..", "a.wav")]
    [InlineData(@"..\..", "a.wav")]
    [InlineData(@"C:\", "a.wav")]
    [InlineData("zo*ne", "a.wav")]
    [InlineData("zone", "/")]
    public void Combine_UnsafeNames_AreRejected(string? subfolder, string name)
    {
        SoundPathResolver.Combine(Base, subfolder, name).Should().BeNull();
    }

    [Fact]
    public void Combine_WithoutBaseDirectory_IsNull()
    {
        SoundPathResolver.Combine("", null, "a.wav").Should().BeNull();
        SoundPathResolver.Combine(null, null, "a.wav").Should().BeNull();
    }

    [Fact]
    public void FindMatches_Wildcards_OnlyPlayableFilesSortedByName()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"omnimud_resolver_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in new[] { "hit2.ogg", "hit1.wav", "HIT3.MP3", "hit4.txt", "hit5.wavx", "miss.wav" })
                File.WriteAllBytes(Path.Combine(dir, name), [1]);

            SoundPathResolver.FindMatches(Path.Combine(dir, "hit*.*")).Select(Path.GetFileName)
                .Should().Equal("hit1.wav", "hit2.ogg", "HIT3.MP3");
            SoundPathResolver.FindMatches(Path.Combine(dir, "hit?.wav")).Select(Path.GetFileName)
                .Should().Equal("hit1.wav");
            SoundPathResolver.FindMatches(Path.Combine(dir, "miss.wav")).Should().ContainSingle();
            SoundPathResolver.FindMatches(Path.Combine(dir, "nothing*.wav")).Should().BeEmpty();
            SoundPathResolver.FindMatches(Path.Combine(dir, "nodir", "*.wav")).Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
