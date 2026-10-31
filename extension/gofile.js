// Hızlı İndir: Gofile desteği (yalıtılmış içerik betiği, yalnızca gofile.io).
// gofile-hook.js'in ilettiği klasör listesinden indirilebilir dosyaları çıkarır ve
// "Hızlı İndir ile indir" düğmesi gösterir. Yalnızca Gofile'ın bu ziyaretçiye zaten
// verdiği bağlantılar kullanılır; dondurulmuş/aşırı yüklenmiş dosyalar ve erişilemeyen
// (şifreli, premium) içerikler atlanır.
(() => {
  if (window.__hizliIndirGofile) return;
  window.__hizliIndirGofile = true;

  function filesOf(data) {
    if (!data || data.canAccess === false) return [];
    const list = data.type === "folder" ? Object.values(data.children || {}) : [data];
    return list
      .filter((c) => c && c.type === "file" && typeof c.link === "string" && /^https:\/\/[a-z0-9-]+\.gofile\.io\//i.test(c.link))
      .filter((c) => !c.isFrozen && !c.overloaded && c.canAccess !== false)
      .map((c) => ({ url: c.link, name: c.name || "", size: c.size || 0 }));
  }
  // Testlerde kullanılmak üzere dışa açılır.
  if (typeof globalThis !== "undefined") globalThis.__hizliIndirGofileFilesOf = filesOf;
  if (typeof chrome === "undefined" || !chrome.runtime?.id) return;

  let files = [];
  let btn = null;

  function render() {
    if (!files.length) { btn?.remove(); btn = null; return; }
    if (!btn) {
      btn = document.createElement("button");
      btn.style.cssText = "position:fixed;right:18px;bottom:18px;z-index:2147483647;padding:10px 16px;border:0;border-radius:10px;background:#0A84FF;color:#fff;font:600 14px system-ui,sans-serif;box-shadow:0 4px 16px rgba(0,0,0,.35);cursor:pointer";
      btn.addEventListener("click", async () => {
        btn.disabled = true;
        const old = btn.textContent;
        btn.textContent = "Gönderiliyor…";
        const r = await chrome.runtime.sendMessage({ type: "gofile-files", items: files, pageUrl: location.href }).catch((e) => ({ ok: false, error: String(e) }));
        btn.textContent = r?.ok ? `Hızlı İndir'e gönderildi (${r.sent})` : `Hata: ${r?.error || "uygulamaya ulaşılamadı"}`;
        setTimeout(() => { if (btn) { btn.textContent = old; btn.disabled = false; } }, 3000);
      });
      document.documentElement.appendChild(btn);
    }
    btn.textContent = files.length === 1 ? "Hızlı İndir ile indir" : `Hızlı İndir ile ${files.length} dosyayı indir`;
  }

  window.addEventListener("message", (e) => {
    if (e.source !== window || e.data?.source !== "hizli-indir-gofile") return;
    files = filesOf(e.data.data);
    render();
  });
  // SPA içinde başka sayfaya geçince düğmeyi kaldır.
  let last = location.href;
  setInterval(() => { if (location.href !== last) { last = location.href; files = []; render(); } }, 800);
})();
