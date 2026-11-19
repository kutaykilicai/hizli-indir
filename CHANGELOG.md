# Değişiklikler

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
