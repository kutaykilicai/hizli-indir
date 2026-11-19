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

public class DhtCacheTests
{
    static readonly System.Net.IPEndPoint A = new(System.Net.IPAddress.Parse("1.2.3.4"), 6881);
    static readonly System.Net.IPEndPoint B = new(System.Net.IPAddress.Parse("5.6.7.8"), 25401);

    [Fact]
    public void BosOnbellegeBaslangicDugumleriEklenir()
    {
        var r = HizliIndir.Core.TorrentEngine.MergeDhtCache(Array.Empty<byte>(), new[] { A, B });
        Assert.Equal(52, r.Length);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 0x1A, 0xE1 }, r[20..26]);
        Assert.Equal(new byte[] { 5, 6, 7, 8, 0x63, 0x39 }, r[46..52]);
    }

    [Fact]
    public void AyniDugumIkiKezEklenmez()
    {
        var once = HizliIndir.Core.TorrentEngine.MergeDhtCache(Array.Empty<byte>(), new[] { A });
        var twice = HizliIndir.Core.TorrentEngine.MergeDhtCache(once, new[] { A, A });
        Assert.Equal(26, twice.Length);
    }

    [Fact]
    public void UzunlukOnekliOnbellekOkunur()
    {
        var raw = HizliIndir.Core.TorrentEngine.MergeDhtCache(Array.Empty<byte>(), new[] { A });
        var bencoded = System.Text.Encoding.ASCII.GetBytes("26:").Concat(raw).ToArray();
        var r = HizliIndir.Core.TorrentEngine.MergeDhtCache(bencoded, new[] { A, B });
        Assert.Equal(52, r.Length);
        Assert.Equal(raw, r[..26]);
    }

    [Fact]
    public void BozukOnbellekAtlanir()
    {
        var r = HizliIndir.Core.TorrentEngine.MergeDhtCache(new byte[] { 9, 9, 9 }, new[] { A });
        Assert.Equal(26, r.Length);
    }
}
