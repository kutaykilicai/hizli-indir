const $ = (id) => document.getElementById(id);
const send = (m) => chrome.runtime.sendMessage(m);
let info = null;

async function load() {
  info = await send({ type: "popup" });
  $("status").textContent = info.online ? "Uygulamaya bağlı" : "Uygulama çalışmıyor ya da köprü kayıtlı değil";
  $("status").className = info.online ? "ok" : "bad";
  $("enabled").checked = info.enabled;
  $("site").checked = info.siteDisabled;
  $("siteLabel").textContent = info.host ? `${info.host} için kapat` : "Bu sitede kapat";
  const box = $("media");
  if (info.media.length) {
    box.innerHTML = "";
    for (const m of info.media.slice().reverse()) {
      const row = document.createElement("div");
      row.className = "media";
      const kind = document.createElement("span"); kind.className = "kind"; kind.textContent = m.kind;
      const u = document.createElement("span"); u.className = "u"; u.textContent = m.url; u.title = m.url;
      const b = document.createElement("button"); b.textContent = "İndir";
      b.onclick = async () => { b.disabled = true; const r = await send({ type: "send-media", url: m.url, pageUrl: info.tabUrl, title: info.title }); b.textContent = r?.ok ? "Gönderildi" : "Olmadı"; };
      row.append(kind, u, b);
      box.append(row);
    }
  }
}

$("enabled").onchange = () => send({ type: "toggle" });
$("site").onchange = () => info?.host && send({ type: "toggle-site", host: info.host });
$("open").onclick = () => send({ type: "open-app" });
$("pageVideo").onclick = async () => {
  const r = await send({ type: "send-media", url: info.tabUrl, pageUrl: info.tabUrl, title: info.title });
  $("pageVideo").textContent = r?.ok ? "Gönderildi" : "Gönderilemedi";
};
load();
