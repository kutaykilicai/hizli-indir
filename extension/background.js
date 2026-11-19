// Hızlı İndir tarayıcı eklentisi: arka plan hizmeti.
// İndirmeleri yakalar, çerez ve yönlendiren bilgisiyle birlikte uygulamaya iletir,
// sayfalardaki video akışlarını kaydeder ve sağ tık menüsünü yönetir.

const HOST = "com.kutaykilic.hizliindir";
const DEFAULT_CONFIG = {
  capture: "3gp 7z aac apk arj avi bin bz2 deb dmg epub exe flac gz img iso jar m4a m4v mkv mov mp3 mp4 mpeg mpg msi msix ogg opus pdf rar rpm tar tgz torrent wav webm wma wmv xz zip zst".split(" "),
  skipSites: [],
  noPanelSites: [],
  minBytes: 0,
  defaultQuality: "1080",
};

let config = { ...DEFAULT_CONFIG };
let appOnline = false;
let lastConfigAt = 0;
let altUntil = 0;              // Alt basılıyken yapılan indirmeler tarayıcıda kalır
const bypass = new Set();      // tarayıcıya geri bırakılan adresler
const postBodies = new Map();  // url -> form gövdesi (POST ile başlayan indirmeler için)
const mediaByTab = new Map();  // tabId -> [{url, type, size}]

// ---------- Ayarlar ----------
async function settings() {
  const s = await chrome.storage.local.get({ enabled: true, disabledSites: [] });
  return s;
}

function native(msg) {
  return new Promise((resolve) => {
    try {
      chrome.runtime.sendNativeMessage(HOST, msg, (reply) => {
        if (chrome.runtime.lastError || !reply) {
          appOnline = false;
          resolve({ ok: false, error: chrome.runtime.lastError?.message || "cevap yok" });
          return;
        }
        appOnline = true;
        resolve(reply);
      });
    } catch (e) {
      appOnline = false;
      resolve({ ok: false, error: String(e) });
    }
  });
}

async function refreshConfig(force = false) {
  if (!force && Date.now() - lastConfigAt < 5 * 60 * 1000) return config;
  const r = await native({ type: "config" });
  if (r.ok) {
    config = {
      capture: (r.capture || DEFAULT_CONFIG.capture).map((x) => String(x).toLowerCase()),
      skipSites: r.skipSites || [],
      noPanelSites: r.noPanelSites || [],
      minBytes: r.minBytes || 0,
      defaultQuality: r.defaultQuality || "1080",
    };
    lastConfigAt = Date.now();
    chrome.storage.local.set({ config });
  }
  return config;
}

chrome.storage.local.get({ config: null }).then((s) => { if (s.config) config = s.config; });

// ---------- Yardımcılar ----------
function wildcard(pattern, text) {
  const rx = new RegExp("^" + pattern.trim().toLowerCase().replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*").replace(/\?/g, ".") + "$");
  return rx.test(text.toLowerCase());
}

function hostOf(url) {
  try { return new URL(url).hostname; } catch { return ""; }
}

function extOf(name) {
  const clean = (name || "").split(/[?#]/)[0];
  const base = clean.split(/[\\/]/).pop() || "";
  const i = base.lastIndexOf(".");
  return i > 0 ? base.slice(i + 1).toLowerCase() : "";
}

function nameFromUrl(url) {
  try {
    const p = new URL(url).pathname.split("/").filter(Boolean).pop() || "";
    return decodeURIComponent(p);
  } catch { return ""; }
}

async function cookieHeader(url) {
  try {
    const list = await chrome.cookies.getAll({ url });
    return list.map((c) => `${c.name}=${c.value}`).join("; ");
  } catch { return ""; }
}

async function netscapeCookies(url) {
  const host = hostOf(url);
  const domains = new Set([host]);
  const parts = host.split(".");
  if (parts.length > 2) domains.add(parts.slice(-2).join("."));
  if (/(^|\.)youtube\.com$|(^|\.)youtu\.be$/.test(host)) { domains.add("youtube.com"); domains.add("google.com"); }
  const seen = new Set();
  const lines = ["# Netscape HTTP Cookie File"];
  for (const d of domains) {
    let list = [];
    try { list = await chrome.cookies.getAll({ domain: d }); } catch { }
    for (const c of list) {
      const key = c.domain + c.path + c.name;
      if (seen.has(key)) continue;
      seen.add(key);
      const dom = c.hostOnly ? c.domain : (c.domain.startsWith(".") ? c.domain : "." + c.domain);
      const exp = c.session ? 0 : Math.floor(c.expirationDate || 0);
      lines.push([dom, c.hostOnly ? "FALSE" : "TRUE", c.path, c.secure ? "TRUE" : "FALSE", exp, c.name, c.value].join("\t"));
    }
  }
  return lines.length > 1 ? lines.join("\n") + "\n" : null;
}

async function siteDisabled(url) {
  const s = await settings();
  const h = hostOf(url);
  return s.disabledSites.includes(h);
}

// ---------- İndirme yakalama ----------
function shouldCapture(item, cfg) {
  const url = item.finalUrl || item.url || "";
  if (!/^https?:/i.test(url)) return false;            // blob:, data: ve file: tarayıcıda kalır
  if (bypass.has(url) || bypass.has(item.url)) return false;
  if (Date.now() < altUntil) return false;
  const host = hostOf(url);
  if (cfg.skipSites.some((p) => wildcard(p, host))) return false;
  const ext = extOf(item.filename) || extOf(nameFromUrl(url));
  if (!ext || !cfg.capture.some((p) => wildcard(p, ext))) return false;
  const size = item.fileSize > 0 ? item.fileSize : item.totalBytes;
  if (cfg.minBytes > 0 && size > 0 && size < cfg.minBytes) return false;
  return true;
}

async function handOver(item) {
  const url = item.finalUrl || item.url;
  let pageUrl = item.referrer || "";
  try {
    const [tab] = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
    if (tab?.url && /^https?:/.test(tab.url)) pageUrl = pageUrl || tab.url;
  } catch { }
  const msg = {
    type: "download",
    url,
    referer: item.referrer || pageUrl || undefined,
    pageUrl: pageUrl || undefined,
    filename: (item.filename || "").split(/[\\/]/).pop() || nameFromUrl(url),
    mime: item.mime,
    size: item.fileSize > 0 ? item.fileSize : (item.totalBytes > 0 ? item.totalBytes : undefined),
    cookies: await cookieHeader(url),
    userAgent: navigator.userAgent,
  };
  const body = postBodies.get(item.url) || postBodies.get(url);
  if (body) msg.postData = body;
  const r = await native(msg);
  if (!r.ok) {
    // Uygulamaya ulaşılamadı: indirmeyi tarayıcıya geri ver.
    bypass.add(url);
    chrome.downloads.download({ url, saveAs: false });
  }
}

chrome.downloads.onDeterminingFilename.addListener((item, suggest) => {
  (async () => {
    const s = await settings();
    if (!s.enabled || (item.referrer && s.disabledSites.includes(hostOf(item.referrer))) || !shouldCapture(item, config)) {
      bypass.delete(item.finalUrl || item.url);
      suggest();
      return;
    }
    try { await chrome.downloads.cancel(item.id); } catch { }
    suggest();
    try { await chrome.downloads.erase({ id: item.id }); } catch { }
    handOver(item);
  })();
  return true;
});

// POST ile başlatılan indirmelerde gövdeyi saklarız; uygulama aynı isteği tekrar yapabilsin.
chrome.webRequest.onBeforeRequest.addListener((d) => {
  if (d.method !== "POST" || !d.requestBody?.formData) return;
  const body = Object.entries(d.requestBody.formData)
    .flatMap(([k, vs]) => vs.map((v) => `${encodeURIComponent(k)}=${encodeURIComponent(v)}`)).join("&");
  postBodies.set(d.url, body);
  if (postBodies.size > 50) postBodies.delete(postBodies.keys().next().value);
}, { urls: ["<all_urls>"], types: ["main_frame", "sub_frame"] }, ["requestBody"]);

// ---------- Video akışlarını izleme ----------
const MEDIA_TYPES = /^(video\/|audio\/|application\/(vnd\.apple\.mpegurl|x-mpegurl|dash\+xml))/i;
const MEDIA_EXT = /\.(m3u8|mpd|mp4|webm|m4v|mov|mkv|mp3|m4a|flv)(\?|$)/i;

chrome.webRequest.onHeadersReceived.addListener((d) => {
  if (d.tabId < 0 || d.statusCode >= 400) return;
  const h = Object.fromEntries((d.responseHeaders || []).map((x) => [x.name.toLowerCase(), x.value || ""]));
  const ct = h["content-type"] || "";
  if (!MEDIA_TYPES.test(ct) && !MEDIA_EXT.test(d.url)) return;
  if (/\.(ts|m4s)(\?|$)|segment|\/range\/|videoplayback/i.test(d.url) && !/\.m3u8|\.mpd/i.test(d.url)) return; // parça istekleri
  const list = mediaByTab.get(d.tabId) || [];
  if (list.some((m) => m.url === d.url)) return;
  const size = parseInt(h["content-length"] || "0", 10);
  const kind = /mpegurl|m3u8/i.test(ct + d.url) ? "HLS" : /dash|\.mpd/i.test(ct + d.url) ? "DASH" : (ct.split("/")[1] || extOf(d.url)).toUpperCase();
  list.push({ url: d.url, kind, size, at: Date.now() });
  if (list.length > 25) list.shift();
  mediaByTab.set(d.tabId, list);
  chrome.action.setBadgeBackgroundColor({ color: "#0A84FF", tabId: d.tabId });
  chrome.action.setBadgeText({ text: String(list.length), tabId: d.tabId });
  chrome.tabs.sendMessage(d.tabId, { type: "media-found", count: list.length }).catch(() => { });
}, { urls: ["<all_urls>"], types: ["media", "xmlhttprequest", "other", "object"] }, ["responseHeaders"]);

chrome.tabs.onUpdated.addListener((tabId, info) => {
  if (info.status === "loading" && info.url) { mediaByTab.delete(tabId); chrome.action.setBadgeText({ text: "", tabId }); }
});
chrome.tabs.onRemoved.addListener((tabId) => mediaByTab.delete(tabId));

// ---------- Video gönderme ----------
async function sendVideo({ pageUrl, target, title, quality, quick }, tabUrl) {
  const page = pageUrl || tabUrl;
  const msg = {
    type: "video",
    pageUrl: target || page,
    url: target || page,
    referer: page,
    title,
    quality: quality || config.defaultQuality,
    cookiesNetscape: await netscapeCookies(target || page),
    userAgent: navigator.userAgent,
    quick: !!quick,
  };
  return native(msg);
}

// ---------- Mesajlar ----------
chrome.runtime.onMessage.addListener((msg, sender, reply) => {
  (async () => {
    switch (msg.type) {
      case "alt":
        altUntil = msg.down ? Date.now() + 4000 : Date.now() + 1500;
        reply({ ok: true });
        break;
      case "state": {
        const s = await settings();
        await refreshConfig();
        const url = sender.tab?.url || msg.url || "";
        const h = hostOf(url);
        reply({
          enabled: s.enabled && !s.disabledSites.includes(h),
          panel: s.enabled && !config.noPanelSites.some((p) => wildcard(p, hostOf(msg.frameUrl || url))) && !s.disabledSites.includes(h),
          defaultQuality: config.defaultQuality,
          media: (mediaByTab.get(sender.tab?.id) || []).map((m) => m.url),
        });
        break;
      }
      case "video":
        reply(await sendVideo(msg, sender.tab?.url));
        break;
      case "links": {
        const ref = sender.tab?.url;
        reply(await native({ type: "links", items: msg.items, referer: ref, cookies: ref ? await cookieHeader(ref) : "", userAgent: navigator.userAgent }));
        break;
      }
      case "popup": {
        const s = await settings();
        const r = await native({ type: "ping" });
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        reply({ online: !!r.ok, enabled: s.enabled, host: hostOf(tab?.url || ""), siteDisabled: s.disabledSites.includes(hostOf(tab?.url || "")), media: mediaByTab.get(tab?.id) || [], tabUrl: tab?.url, title: tab?.title });
        break;
      }
      case "toggle": {
        const s = await settings();
        await chrome.storage.local.set({ enabled: !s.enabled });
        reply({ enabled: !s.enabled });
        break;
      }
      case "toggle-site": {
        const s = await settings();
        const list = s.disabledSites.includes(msg.host) ? s.disabledSites.filter((x) => x !== msg.host) : [...s.disabledSites, msg.host];
        await chrome.storage.local.set({ disabledSites: list });
        reply({ siteDisabled: list.includes(msg.host) });
        break;
      }
      case "send-media":
        reply(await sendVideo({ pageUrl: msg.pageUrl, target: msg.url, title: msg.title, quality: msg.quality }, msg.pageUrl));
        break;
      case "magnet":
        reply(/^magnet:\?/i.test(msg.url || "") ? await native({ type: "torrent", magnet: msg.url }) : { ok: false });
        break;
      case "open-app":
        reply(await native({ type: "show" }));
        break;
      default:
        reply({ ok: false });
    }
  })();
  return true;
});

// ---------- Sağ tık menüsü ----------
function buildMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: "link", title: "Hızlı İndir ile indir", contexts: ["link"] });
    chrome.contextMenus.create({ id: "media", title: "Bu videoyu Hızlı İndir ile indir", contexts: ["video", "audio"] });
    chrome.contextMenus.create({ id: "image", title: "Resmi Hızlı İndir ile indir", contexts: ["image"] });
    chrome.contextMenus.create({ id: "selection", title: "Seçili bağlantıları Hızlı İndir ile indir", contexts: ["selection"] });
    chrome.contextMenus.create({ id: "all", title: "Sayfadaki tüm bağlantıları Hızlı İndir ile indir", contexts: ["page"] });
    chrome.contextMenus.create({ id: "pagevideo", title: "Bu sayfadaki videoyu indir", contexts: ["page"], documentUrlPatterns: ["*://*.youtube.com/*", "*://youtu.be/*", "*://*.vimeo.com/*", "*://*.dailymotion.com/*", "*://*.twitch.tv/*", "*://x.com/*", "*://*.instagram.com/*", "*://*.tiktok.com/*"] });
  });
}
chrome.runtime.onInstalled.addListener(() => { buildMenus(); refreshConfig(true); });
chrome.runtime.onStartup.addListener(() => { buildMenus(); refreshConfig(true); });

async function collectLinks(tabId, frameId, onlySelection) {
  const [res] = await chrome.scripting.executeScript({
    target: { tabId, frameIds: [frameId || 0] },
    args: [onlySelection],
    func: (sel) => {
      let anchors = [...document.querySelectorAll("a[href]")];
      if (sel) {
        const s = getSelection();
        anchors = anchors.filter((a) => s && s.rangeCount && s.containsNode(a, true));
      }
      return anchors.map((a) => ({ url: a.href, text: (a.textContent || "").trim().slice(0, 200) })).filter((x) => /^https?:/.test(x.url));
    },
  });
  return res?.result || [];
}

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  const page = info.pageUrl || tab?.url;
  const send = async (url, extra = {}) => native({
    type: "download", url, referer: info.frameUrl || page, pageUrl: page,
    cookies: await cookieHeader(url), userAgent: navigator.userAgent, ...extra,
  });
  switch (info.menuItemId) {
    case "link": await send(info.linkUrl); break;
    case "image": await send(info.srcUrl); break;
    case "media":
      if (info.srcUrl && /^https?:/.test(info.srcUrl) && !/youtube\.com/.test(page)) await send(info.srcUrl);
      else await sendVideo({ pageUrl: page }, page);
      break;
    case "pagevideo": await sendVideo({ pageUrl: page, title: tab?.title }, page); break;
    case "selection":
    case "all": {
      const items = await collectLinks(tab.id, info.frameId, info.menuItemId === "selection");
      if (items.length) await native({ type: "links", items, referer: page, cookies: await cookieHeader(page), userAgent: navigator.userAgent });
      break;
    }
  }
});
