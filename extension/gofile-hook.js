// Hızlı İndir: Gofile sayfa kancası (MAIN dünyası, yalnızca gofile.io).
// Gofile kendi sitesinde klasör içeriğini https://api.gofile.io/contents/<id> ile alır
// (misafir hesap belirteci + sitenin ürettiği X-Website-Token ile). Biz bu isteği yeniden
// üretmeyiz; sitenin kendi aldığı yanıtın bir kopyasını okuyup eklentinin yalıtılmış
// içerik betiğine iletiriz. Böylece şifre, premium ve hız sınırı kararları tamamen Gofile'da kalır.
(() => {
  if (window.__hizliIndirGofileHook) return;
  window.__hizliIndirGofileHook = true;
  const RX = /^https:\/\/api\.gofile\.io\/contents\/[^/?#]+(\?|$)/;
  const orig = window.fetch;
  window.fetch = async function (...args) {
    const res = await orig.apply(this, args);
    try {
      const url = typeof args[0] === "string" ? args[0] : args[0]?.url || String(args[0]);
      if (RX.test(url) && res.ok) {
        res.clone().json().then((body) => {
          if (body && body.status === "ok" && body.data) {
            window.postMessage({ source: "hizli-indir-gofile", data: body.data }, location.origin);
          }
        }).catch(() => { });
      }
    } catch { }
    return res;
  };
})();
