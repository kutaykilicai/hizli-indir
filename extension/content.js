// Hızlı İndir: sayfa betiği.
// Videoların üzerinde "Bu videoyu indir" düğmesi gösterir ve Alt tuşunu arka plana bildirir.
(() => {
  if (window.__hizliIndir) return;
  window.__hizliIndir = true;

  const QUALITIES = [
    ["best", "En iyi"], ["2160", "4K"], ["1440", "1440p"], ["1080", "1080p"], ["720", "720p"], ["480", "480p"], ["mp3", "MP3"], ["m4a", "M4A"],
  ];
  const isYouTube = /(^|\.)youtube\.com$|(^|\.)youtube-nocookie\.com$/.test(location.hostname);
  let state = { enabled: true, panel: true, defaultQuality: "1080" };
  let host, shadow, button, menu, current = null, hideTimer = 0, menuOpen = false;

  function send(msg) {
    try { return chrome.runtime.sendMessage(msg); } catch { return Promise.resolve(null); }
  }

  // ---- Alt tuşu: basılıyken yapılan indirmeler tarayıcıda kalır ----
  let altDown = false;
  addEventListener("keydown", (e) => { if (e.key === "Alt" && !altDown) { altDown = true; send({ type: "alt", down: true }); } }, true);
  addEventListener("keyup", (e) => { if (e.key === "Alt" && altDown) { altDown = false; send({ type: "alt", down: false }); } }, true);
  addEventListener("mousedown", (e) => { if (e.altKey) send({ type: "alt", down: true }); }, true);

  async function loadState() {
    const r = await send({ type: "state", url: top === self ? location.href : undefined, frameUrl: location.href });
    if (r) state = r;
  }

  // ---- Düğme ----
  function build() {
    host = document.createElement("hizli-indir-panel");
    host.style.cssText = "all:initial;position:fixed;z-index:2147483646;top:0;left:0;width:0;height:0;";
    shadow = host.attachShadow({ mode: "closed" });
    shadow.innerHTML = `
      <style>
        :host { all: initial; }
        .wrap { position: fixed; font: 13px/1.2 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif; opacity: 0; transform: translateY(-4px); transition: opacity .18s ease, transform .18s ease; pointer-events: none; }
        .wrap.show { opacity: 1; transform: none; pointer-events: auto; }
        .btn { display: flex; align-items: center; gap: 7px; border: 0; cursor: pointer; padding: 7px 13px 7px 10px; border-radius: 999px;
               background: rgba(10,132,255,.94); color: #fff; font: inherit; font-weight: 600; box-shadow: 0 4px 16px rgba(0,0,0,.28); backdrop-filter: blur(10px); }
        .btn:hover { background: #0A84FF; }
        .btn svg { width: 15px; height: 15px; }
        .x { margin-left: 4px; opacity: .75; font-weight: 400; padding: 0 2px; }
        .x:hover { opacity: 1; }
        .menu { margin-top: 6px; background: rgba(28,28,30,.92); color: #f5f5f7; border-radius: 12px; padding: 5px; display: none; min-width: 170px;
                box-shadow: 0 10px 30px rgba(0,0,0,.35); backdrop-filter: blur(16px); }
        .menu.open { display: block; }
        .item { padding: 7px 10px; border-radius: 8px; cursor: pointer; display: flex; justify-content: space-between; }
        .item:hover { background: rgba(255,255,255,.12); }
        .item small { opacity: .6; }
        .note { padding: 6px 10px 4px; font-size: 11px; opacity: .6; }
      </style>
      <div class="wrap">
        <button class="btn" part="btn"><svg viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><path d="M8 2v8M4.5 6.8 8 10.3l3.5-3.5M3 13.5h10"/></svg><span>Bu videoyu indir</span><span class="x" title="Gizle">✕</span></button>
        <div class="menu"></div>
      </div>`;
    const wrap = shadow.querySelector(".wrap");
    button = shadow.querySelector(".btn");
    menu = shadow.querySelector(".menu");
    menu.innerHTML = QUALITIES.map(([k, l]) => `<div class="item" data-q="${k}">${l}${k === state.defaultQuality ? "<small>varsayılan</small>" : ""}</div>`).join("") +
      `<div class="note">Alt+tık: varsayılan kaliteyle hemen başlat</div>`;
    button.addEventListener("click", (e) => {
      e.preventDefault(); e.stopPropagation();
      if (e.target.classList.contains("x")) { wrap.classList.remove("show"); current && (current.__hizliHidden = true); return; }
      if (e.altKey) { start(state.defaultQuality, true); return; }
      menuOpen = !menuOpen;
      menu.classList.toggle("open", menuOpen);
    });
    menu.addEventListener("click", (e) => {
      const q = e.target.closest(".item")?.dataset.q;
      if (!q) return;
      e.stopPropagation();
      start(q, false);
    });
    host.addEventListener("mouseenter", () => clearTimeout(hideTimer));
    host.addEventListener("mouseleave", () => scheduleHide());
    (document.documentElement || document.body).appendChild(host);
    return wrap;
  }

  function title() {
    if (isYouTube) {
      const h = document.querySelector("h1.ytd-watch-metadata yt-formatted-string, h1.title");
      if (h?.textContent?.trim()) return h.textContent.trim();
      return document.title.replace(/\s*-\s*YouTube\s*$/, "");
    }
    return document.querySelector('meta[property="og:title"]')?.content || document.title;
  }

  function targetFor(video) {
    // YouTube ve bilinen siteler sayfa adresiyle, diğerlerinde doğrudan dosya varsa o adresle indirilir.
    if (isYouTube || top !== self && /youtube/.test(location.hostname)) return location.href;
    const src = video.currentSrc || video.src || video.querySelector("source")?.src || "";
    if (/^https?:/.test(src)) return src;
    if (state.media?.length) return state.media[state.media.length - 1];
    return location.href;
  }

  async function start(quality, quick) {
    if (!current) return;
    menuOpen = false; menu.classList.remove("open");
    const label = button.querySelector("span");
    label.textContent = "Gönderiliyor";
    const r = await send({ type: "video", pageUrl: location.href, target: targetFor(current), title: title(), quality, quick });
    label.textContent = r?.ok ? "Hızlı İndir'e gönderildi" : "Hızlı İndir'e ulaşılamadı";
    setTimeout(() => (label.textContent = "Bu videoyu indir"), 2500);
  }

  function visible(v) {
    const r = v.getBoundingClientRect();
    return r.width >= 200 && r.height >= 112 && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth && getComputedStyle(v).visibility !== "hidden";
  }

  function place(v) {
    const wrap = shadow.querySelector(".wrap");
    let r = v.getBoundingClientRect();
    if (isYouTube) { const p = v.closest("#movie_player, .html5-video-player"); if (p) r = p.getBoundingClientRect(); }
    const top = Math.max(8, r.top + 12);
    const right = Math.min(innerWidth - 8, r.right - 12);
    wrap.style.top = top + "px";
    wrap.style.left = "auto";
    wrap.style.right = Math.max(8, innerWidth - right) + "px";
  }

  function show(v) {
    if (!state.enabled || !state.panel || v.__hizliHidden) return;
    if (!host) build();
    current = v;
    place(v);
    shadow.querySelector(".wrap").classList.add("show");
    scheduleHide();
  }

  function scheduleHide() {
    clearTimeout(hideTimer);
    hideTimer = setTimeout(() => {
      if (menuOpen || !shadow) return;
      const v = current;
      if (v && !v.paused && isYouTube) return; // oynatılırken YouTube'da görünür kalır
      shadow.querySelector(".wrap").classList.remove("show");
    }, 3500);
  }

  function scan() {
    if (!state.enabled || !state.panel) return;
    const vids = [...document.querySelectorAll("video")].filter(visible);
    if (!vids.length) {
      if (shadow && !menuOpen) shadow.querySelector(".wrap").classList.remove("show");
      return;
    }
    const best = vids.sort((a, b) => b.clientWidth * b.clientHeight - a.clientWidth * a.clientHeight)[0];
    if (!best.__hizliBound) {
      best.__hizliBound = true;
      const hoverTarget = isYouTube ? (best.closest("#movie_player") || best) : (best.parentElement || best);
      hoverTarget.addEventListener("mousemove", () => show(best), { passive: true });
      best.addEventListener("play", () => show(best));
    }
    if (current === best && shadow) place(best);
    else if (!best.paused) show(best);
  }

  loadState().then(() => {
    scan();
    setInterval(scan, 1000);
    addEventListener("scroll", () => current && shadow && place(current), { passive: true });
    addEventListener("resize", () => current && shadow && place(current));
    document.addEventListener("yt-navigate-finish", () => { if (current) current.__hizliHidden = false; loadState(); });
    document.addEventListener("click", () => { if (menuOpen) { menuOpen = false; menu.classList.remove("open"); } });
  });

  chrome.runtime.onMessage.addListener((m) => {
    if (m.type === "media-found") loadState().then(scan);
  });
})();
