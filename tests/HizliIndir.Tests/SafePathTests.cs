using HizliIndir.Core;
using Xunit;

public class SafePathTests
{
    [Theory]
    [InlineData("film.mkv")]
    [InlineData("Klasör adı")]
    [InlineData("a.b.c")]
    [InlineData(".gizli")]
    public void GuvenliAdlarKabulEdilir(string name) => Assert.True(SafePath.IsSafeName(name));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:")]
    [InlineData("dosya.txt:gizli")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("LPT1")]
    [InlineData("nokta.")]
    [InlineData("bosluk ")]
    [InlineData("kontrol\u0001")]
    [InlineData("soru?")]
    public void GuvensizAdlarReddedilir(string name) => Assert.False(SafePath.IsSafeName(name));

    [Fact]
    public void UzunAdReddedilir() => Assert.False(SafePath.IsSafeName(new string('a', 256)));

    [Fact]
    public void BirlesimKokunAltindaKalir()
    {
        var root = Path.Combine(Path.GetTempPath(), "hi-kok");
        var p = SafePath.Combine(root, new[] { "Klasör", "dosya.bin" });
        Assert.NotNull(p);
        Assert.StartsWith(Path.GetFullPath(root), p);
        Assert.Null(SafePath.Combine(root, new[] { "..", "dosya.bin" }));
        Assert.Null(SafePath.Combine(root, Array.Empty<string>()));
    }
}
