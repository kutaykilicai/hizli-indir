# Değişiklikler

## 1.1.2

- "Invalid BEncodedString" hatasıyla açılmayan .torrent dosyaları için düzeltme. Sorunlu dosyalar diskte zaten yarımdı: 1.1.0 öncesinde .torrent bağlantıları sıradan dosya gibi bölümlü indiriliyordu ve kaydedilen dosya, içinde bildirilen parça listesinden çok daha kısa kalmıştı.
- Web'den gelen .torrent dosyası (eklentinin yakaladığı, adres olarak eklenen) artık tek istekle sonuna kadar okunuyor. Bildirilen boyuttan az veri gelirse, bağlantı yarıda koparsa ya da sunucu torrent yerine web sayfası döndürürse açık bir Türkçe hata gösteriliyor. Sıkıştırılmış (gzip, deflate, br) yanıtlar da açılıyor.
- Dosyadan açarken ya da sürükleyip bırakırken torrent yarımsa ve dosya Hızlı İndir ile indirilmişse, kaynak adresinden yeniden alınıyor. Kaynağı bilinmeyen yarım dosyada "dosya eksik, yeniden indirin" uyarısı çıkıyor.
- Testler: kesik dosya, 100 KB'tan büyük torrent, parça parça inen, uzunluğu bildirilmeyen, sıkıştırılmış, yarıda kopan ve HTML dönen URL indirmeleri.

## 1.1.1

- Magnet bağlantılarında dosya listesinin hiç gelmemesi düzeltildi. MonoTorrent DHT'yi yalnızca router.bittorrent.com ile başlatıyordu; bu sunucu yanıt vermeyince DHT hiç düğüm bulamıyor, eşleri yalnızca DHT'de olan magnet bağlantıları (Ubuntu'nun tracker'ı yalnızca kendi sunucusunu veriyor) üç dakika sonra zaman aşımına düşüyordu. Artık motor ilk çalışmadan önce dht.transmissionbt.com, dht.libtorrent.org, router.bittorrent.com ve router.utorrent.com başlangıç düğümü olarak ekleniyor.
- DHT önbelleği okunurken uzunluk önekli kayıtlar da tanınıyor.

## 1.1.0

- Torrent desteği eklendi. `.torrent` dosyası ya da magnet bağlantısıyla indirme başlatılabiliyor.
  - Dosya seçimi: torrentteki dosyalardan yalnızca istenenler iner.
  - Duraklat ve devam: kaldığı yerden devam bilgisi uygulama kapanınca da korunur.
  - İlerleme penceresinde dosya bazında yüzde ve inen parçaların haritası görünür.
  - Ana pencerede yeni **Torrent** düğmesi, `.torrent` sürükle bırak ve **Adres ekle** penceresinde magnet desteği.
  - Tarayıcı eklentisi magnet bağlantılarını ve `.torrent` indirmelerini uygulamaya gönderir. Alt ile tıklanırsa tarayıcıda kalır.
  - Ayarlar'a Torrent sayfası eklendi: bağlantı noktası ve bitince paylaşmayı sürdürme.
  - Torrent içindeki dosya yolları denetlenir. `..`, sürücü harfi, `:` ya da Windows'un ayrılmış adlarını (CON, NUL, COM1 gibi) içeren torrentler reddedilir.
- Kategorilere Torrent eklendi.
- Torrent motoru olarak MonoTorrent 3.0.2 (MIT) kullanılıyor.
- İlk test projesi eklendi: `tests/HizliIndir.Tests`.

## 1.0.0

- İlk açık kaynak sürüm.
