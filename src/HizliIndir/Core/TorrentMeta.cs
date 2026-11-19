using MonoTorrent;
using MonoTorrent.BEncoding;

namespace HizliIndir.Core;

/// <summary>
/// .torrent içeriğini okur ve motor dosyaya dokunmadan önce her dosya yolunu denetler.
/// Ham sözlükten okunur, çünkü kütüphanenin kendi düzeltmesine güvenmek yerine kötü
/// niyetli bir yolu tamamen reddetmek istiyoruz.
/// </summary>
public sealed class TorrentMeta
{
    public required Torrent Torrent { get; init; }
    public required byte[] Bytes { get; init; }
    public string InfoHash => Torrent.InfoHashes.V1OrV2.ToHex().ToLowerInvariant();
    public string Name => Torrent.Name;

    public const int MaxBytes = 20 * 1024 * 1024;

    public static TorrentMeta Parse(byte[] data)
    {
        if (data.Length == 0 || data.Length > MaxBytes) throw new InvalidDataException("Torrent dosyası geçersiz boyutta");
        BEncodedDictionary root;
        try { root = BEncodedValue.Decode<BEncodedDictionary>(data); }
        catch (Exception ex)
        {
            // Bencode sözlüğü 'e' ile biter; değilse dosyanın sonu kesilmiştir (yarım inmiş ya da sunucu eksik göndermiş).
            if (data[^1] != (byte)'e')
                throw new InvalidDataException($"Torrent dosyası eksik: {data.Length} bayt ve sonu kesik. Dosyayı kaynağından yeniden indirin.");
            throw new InvalidDataException("Torrent dosyası okunamadı: " + ex.Message);
        }
        if (Get(root, "info") is not BEncodedDictionary info) throw new InvalidDataException("Torrent dosyasında bilgi bölümü yok");
        CheckPaths(info);
        if (!Torrent.TryLoad(data, out var t) || t == null) throw new InvalidDataException("Torrent dosyası okunamadı");
        return new TorrentMeta { Torrent = t, Bytes = data };
    }

    public static TorrentMeta Load(string path) => Parse(File.ReadAllBytes(path));

    static void CheckPaths(BEncodedDictionary info)
    {
        var name = Str(info, "name.utf-8") ?? Str(info, "name");
        if (!SafePath.IsSafeName(name)) throw Unsafe(name ?? "(adsız)");

        if (Get(info, "files") is BEncodedList files)
        {
            foreach (var f in files.OfType<BEncodedDictionary>())
            {
                foreach (var key in new[] { "path.utf-8", "path" })
                {
                    if (Get(f, key) is not BEncodedList parts) continue;
                    var list = parts.Select(p => p is BEncodedString s ? s.Text : "").ToList();
                    // BEP 47 dolgu dosyaları da aynı kurala uyar.
                    if (list.Count == 0 || list.Any(p => !SafePath.IsSafeName(p))) throw Unsafe(string.Join("/", list));
                }
            }
        }
        if (Get(info, "file tree") is BEncodedDictionary tree) CheckTree(tree, name!);
    }

    // BitTorrent v2: iç içe sözlük; boş anahtar dosyanın kendisidir.
    static void CheckTree(BEncodedDictionary node, string at)
    {
        foreach (var (k, v) in node)
        {
            if (k.Text.Length == 0) continue;
            if (!SafePath.IsSafeName(k.Text)) throw Unsafe(at + "/" + k.Text);
            if (v is BEncodedDictionary child) CheckTree(child, at + "/" + k.Text);
        }
    }

    static BEncodedValue? Get(BEncodedDictionary d, string key) => d.TryGetValue(key, out var v) ? v : null;
    static string? Str(BEncodedDictionary d, string key) => Get(d, key) is BEncodedString s ? s.Text : null;
    static InvalidDataException Unsafe(string p) => new("Güvensiz dosya yolu reddedildi: " + p);

    /// <summary>Metin bir magnet bağlantısı mı?</summary>
    public static bool IsMagnet(string? text) =>
        text != null && text.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) && MagnetLink.TryParse(text.Trim(), out _);

    public static bool IsTorrentName(string? name) =>
        name != null && name.Split('?', '#')[0].EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);
}
