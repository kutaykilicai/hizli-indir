using HizliIndir.Core;
using MonoTorrent.BEncoding;
using Xunit;

public class TorrentMetaTests
{
    static BEncodedList Path(params string[] parts) => new(parts.Select(p => (BEncodedValue)new BEncodedString(p)));

    static byte[] Build(Action<BEncodedDictionary> info)
    {
        var d = new BEncodedDictionary
        {
            ["piece length"] = new BEncodedNumber(16384),
            ["pieces"] = new BEncodedString(new byte[20]),
        };
        info(d);
        return new BEncodedDictionary { ["announce"] = new BEncodedString("http://127.0.0.1:1/announce"), ["info"] = d }.Encode();
    }

    static byte[] Multi(params BEncodedList[] paths) => Build(d =>
    {
        d["name"] = new BEncodedString("Paket");
        d["files"] = new BEncodedList(paths.Select(p => (BEncodedValue)new BEncodedDictionary { ["length"] = new BEncodedNumber(100), ["path"] = p }));
    });

    [Fact]
    public void TekDosyaliTorrentOkunur()
    {
        var data = Build(d => { d["name"] = new BEncodedString("deneme.bin"); d["length"] = new BEncodedNumber(1000); });
        var m = TorrentMeta.Parse(data);
        Assert.Equal("deneme.bin", m.Name);
        Assert.Equal(40, m.InfoHash.Length);
        Assert.Single(m.Torrent.Files);
    }

    [Fact]
    public void CokDosyaliTorrentOkunur()
    {
        var m = TorrentMeta.Parse(Multi(Path("Alt", "a.txt"), Path("b.txt")));
        Assert.Equal(2, m.Torrent.Files.Count);
    }

    [Theory]
    [InlineData("..", "kotu.exe")]
    [InlineData("Alt", "..", "..", "kotu.exe")]
    [InlineData("C:", "Windows", "kotu.dll")]
    [InlineData("CON")]
    [InlineData("a/../../b")]
    [InlineData("ad:akis")]
    public void KotuNiyetliYolReddedilir(params string[] parts)
    {
        var ex = Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(Multi(Path("iyi.txt"), Path(parts))));
        Assert.Contains("Güvensiz", ex.Message);
    }

    [Fact]
    public void BosYolReddedilir() =>
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(Multi(Path())));

    [Fact]
    public void GuvensizAdReddedilir()
    {
        var data = Build(d => { d["name"] = new BEncodedString(".."); d["length"] = new BEncodedNumber(10); });
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(data));
    }

    [Fact]
    public void Utf8YoluDaDenetlenir()
    {
        var data = Build(d =>
        {
            d["name"] = new BEncodedString("Paket");
            d["files"] = new BEncodedList { new BEncodedDictionary { ["length"] = new BEncodedNumber(10), ["path"] = Path("iyi.txt"), ["path.utf-8"] = Path("..", "kotu.txt") } };
        });
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(data));
    }

    [Fact]
    public void V2DosyaAgaciDenetlenir()
    {
        var data = Build(d =>
        {
            d["name"] = new BEncodedString("Paket");
            d["length"] = new BEncodedNumber(10);
            d["file tree"] = new BEncodedDictionary { [".."] = new BEncodedDictionary { [""] = new BEncodedDictionary { ["length"] = new BEncodedNumber(10) } } };
        });
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(data));
    }

    [Fact]
    public void BozukVeriReddedilir()
    {
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(new byte[] { 1, 2, 3 }));
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(new BEncodedDictionary { ["a"] = new BEncodedNumber(1) }.Encode()));
    }

    [Theory]
    [InlineData("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=deneme", true)]
    [InlineData("magnet:?dn=hash-yok", false)]
    [InlineData("https://ornek.com/dosya.torrent", false)]
    [InlineData(null, false)]
    public void MagnetTanima(string? text, bool expected) => Assert.Equal(expected, TorrentMeta.IsMagnet(text));

    [Theory]
    [InlineData("ubuntu.iso.torrent", true)]
    [InlineData("https://ornek.com/a.TORRENT?x=1", true)]
    [InlineData("dosya.torrent.exe", false)]
    public void TorrentAdiTanima(string name, bool expected) => Assert.Equal(expected, TorrentMeta.IsTorrentName(name));
}
