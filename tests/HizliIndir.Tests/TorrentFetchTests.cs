using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using HizliIndir.Core;
using MonoTorrent.BEncoding;
using Xunit;

public class TorrentFetchTests
{
    /// <summary>100 KB'tan büyük, geçerli bir tek dosyalı torrent (6000 parça).</summary>
    internal static byte[] Large()
    {
        var info = new BEncodedDictionary
        {
            ["name"] = new BEncodedString("buyuk.bin"),
            ["length"] = new BEncodedNumber(6000L * 16384),
            ["piece length"] = new BEncodedNumber(16384),
            ["pieces"] = new BEncodedString(new byte[6000 * 20]),
        };
        return new BEncodedDictionary { ["announce"] = new BEncodedString("http://127.0.0.1:1/announce"), ["info"] = info }.Encode();
    }

    /// <summary>Gövdeyi küçük parçalar hâlinde veren, ağdan inen dosyayı taklit eden akış.</summary>
    sealed class Trickle(byte[] data, int chunk, int failAfter = -1) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (failAfter >= 0 && Position >= failAfter) throw new IOException("bağlantı koptu");
            return base.Read(buffer, offset, Math.Min(count, chunk));
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tmp = new byte[Math.Min(buffer.Length, chunk)];
            int r = Read(tmp, 0, tmp.Length);
            tmp.AsSpan(0, r).CopyTo(buffer.Span);
            return ValueTask.FromResult(r);
        }
    }

    sealed class Fake(Func<HttpResponseMessage> make) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(make());
    }

    static HttpResponseMessage Res(Stream body, long? length, string type = "application/x-bittorrent", string? enc = null)
    {
        var c = new StreamContent(body);
        c.Headers.ContentType = new MediaTypeHeaderValue(type);
        c.Headers.ContentLength = length;
        if (enc != null) c.Headers.ContentEncoding.Add(enc);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
    }

    static Task<TorrentMeta> Fetch(Func<HttpResponseMessage> make) =>
        TorrentFetch.DownloadAsync(new HttpClient(new Fake(make)), new HttpRequestMessage(HttpMethod.Get, "https://ornek.test/a.torrent"));

    [Fact]
    public void BuyukTorrentDosyasiOkunur()
    {
        var data = Large();
        Assert.True(data.Length > 100 * 1024);
        Assert.Equal("buyuk.bin", TorrentMeta.Parse(data).Name);
    }

    [Fact]
    public void KesikDosyaAcikHataVerir()
    {
        var data = Large();
        var cut = data.AsSpan(0, 2118).ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => TorrentMeta.Parse(cut));
        Assert.Contains("eksik", ex.Message);
    }

    [Fact]
    public async Task UrldenParcaParcaInenBuyukDosyaTamOkunur()
    {
        var data = Large();
        var m = await Fetch(() => Res(new Trickle(data, 1500), data.Length));
        Assert.Equal(data.Length, m.Bytes.Length);
        Assert.Equal("buyuk.bin", m.Name);
    }

    [Fact]
    public async Task UzunlukBildirilmeyenYanitTamOkunur()
    {
        var data = Large();
        var m = await Fetch(() => Res(new Trickle(data, 4096), null));
        Assert.Equal(data.Length, m.Bytes.Length);
    }

    [Fact]
    public async Task SikistirilmisYanitAcilir()
    {
        var data = Large();
        var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest, true)) gz.Write(data);
        var packed = ms.ToArray();
        var m = await Fetch(() => Res(new MemoryStream(packed), packed.Length, enc: "gzip"));
        Assert.Equal(data.Length, m.Bytes.Length);
    }

    [Fact]
    public async Task YarimKalanIndirmeAcikHataVerir()
    {
        var data = Large();
        var ex = await Assert.ThrowsAsync<IOException>(() => Fetch(() => Res(new Trickle(data, 1500, failAfter: 2118), data.Length)));
        Assert.Contains("yarım", ex.Message);
    }

    [Fact]
    public async Task EksikGovdeBildirilenUzunluklaYakalanir()
    {
        var data = Large();
        var cut = data.AsSpan(0, 2118).ToArray();
        var ex = await Assert.ThrowsAsync<IOException>(() => Fetch(() => Res(new MemoryStream(cut), data.Length)));
        Assert.Contains("yarım", ex.Message);
    }

    [Fact]
    public async Task WebSayfasiTorrentSanilmaz()
    {
        var html = "<!doctype html><html><body>giris</body></html>"u8.ToArray();
        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => Fetch(() => Res(new MemoryStream(html), html.Length, "application/octet-stream")));
        Assert.Contains("web sayfası", ex.Message);
    }
}
