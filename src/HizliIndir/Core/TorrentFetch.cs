using System.IO.Compression;
using System.Net.Http;

namespace HizliIndir.Core;

/// <summary>
/// Web'deki .torrent dosyasını bölümlü indirme kullanmadan tek istekle, sonuna kadar okur.
/// Sunucu dosyayı yarım gönderirse ya da torrent yerine web sayfası dönerse açık bir hata verir.
/// </summary>
public static class TorrentFetch
{
    public static async Task<TorrentMeta> DownloadAsync(HttpClient client, HttpRequestMessage req, CancellationToken ct = default)
    {
        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw new IOException($"Sunucu {(int)res.StatusCode} döndürdü");
        var declared = res.Content.Headers.ContentLength;
        if (declared > TorrentMeta.MaxBytes) throw new InvalidDataException("Torrent dosyası çok büyük");

        byte[] body;
        await using (var raw = await res.Content.ReadAsStreamAsync(ct))
        using (var ms = new MemoryStream())
        {
            try { await CopyLimitedAsync(raw, ms, ct); }
            catch (Exception ex) when (ex is IOException or HttpRequestException && !ct.IsCancellationRequested)
            {
                throw new IOException($"Sunucu dosyayı yarım gönderdi ({ms.Length} bayt alındı). Yeniden deneyin.", ex);
            }
            body = ms.ToArray();
        }
        if (declared is long n && body.Length != n)
            throw new IOException($"Sunucu dosyayı yarım gönderdi ({body.Length}/{n} bayt). Yeniden deneyin.");

        // Accept-Encoding: identity istesek de sıkıştırarak gönderen sunucular var.
        var enc = res.Content.Headers.ContentEncoding.LastOrDefault()?.ToLowerInvariant();
        if (enc is "gzip" or "deflate" or "br") body = Decompress(body, enc);

        var type = res.Content.Headers.ContentType?.MediaType ?? "";
        if (type.Contains("html") || LooksLikeHtml(body))
            throw new InvalidDataException("Sunucu torrent yerine bir web sayfası döndürdü. Sitede giriş ya da doğrulama gerekiyor olabilir; bağlantıya tarayıcıdan yeniden tıklayın.");
        return TorrentMeta.Parse(body);
    }

    static async Task CopyLimitedAsync(Stream src, Stream dst, CancellationToken ct)
    {
        var buf = new byte[81920];
        int r;
        while ((r = await src.ReadAsync(buf, ct)) > 0)
        {
            if (dst.Length + r > TorrentMeta.MaxBytes) throw new InvalidDataException("Torrent dosyası çok büyük");
            await dst.WriteAsync(buf.AsMemory(0, r), ct);
        }
    }

    static byte[] Decompress(byte[] data, string enc)
    {
        using var input = new MemoryStream(data);
        using Stream z = enc switch
        {
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            "deflate" => new ZLibStream(input, CompressionMode.Decompress),
            _ => new BrotliStream(input, CompressionMode.Decompress),
        };
        using var output = new MemoryStream();
        var buf = new byte[81920];
        int r;
        while ((r = z.Read(buf)) > 0)
        {
            if (output.Length + r > TorrentMeta.MaxBytes) throw new InvalidDataException("Torrent dosyası çok büyük");
            output.Write(buf, 0, r);
        }
        return output.ToArray();
    }

    static bool LooksLikeHtml(byte[] b)
    {
        int i = 0;
        while (i < b.Length && i < 64 && (b[i] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0xEF or 0xBB or 0xBF)) i++;
        return i < b.Length && b[i] == (byte)'<';
    }
}
