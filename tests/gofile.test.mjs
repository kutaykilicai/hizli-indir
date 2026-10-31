// node tests/gofile.test.mjs — Gofile ayrıştırma ve kanca testleri (sahte yanıtlarla).
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";

const ext = new URL("../extension/", import.meta.url);
const read = (f) => fs.readFileSync(new URL(f, ext), "utf8");

// 1) gofile.js: filesOf
const g = { window: {}, location: { href: "https://gofile.io/d/abc" } };
g.globalThis = g; g.window = g;
vm.runInNewContext(read("gofile.js"), g);
const filesOf = g.__hizliIndirGofileFilesOf;
const folder = {
  type: "folder", canAccess: true, id: "f1",
  children: {
    a: { type: "file", name: "a.bin", size: 10, link: "https://store3.gofile.io/download/web/a/a.bin" },
    b: { type: "folder", name: "alt" },
    c: { type: "file", name: "frozen.bin", isFrozen: true, link: "https://store3.gofile.io/download/web/c/frozen.bin" },
    d: { type: "file", name: "over.bin", overloaded: true, link: "https://store3.gofile.io/download/web/d/over.bin" },
    e: { type: "file", name: "evil.bin", link: "https://evil.example/download/x" },
  },
};
assert.deepEqual(JSON.parse(JSON.stringify(filesOf(folder))), [{ url: "https://store3.gofile.io/download/web/a/a.bin", name: "a.bin", size: 10 }]);
assert.deepEqual(filesOf({ type: "folder", canAccess: false, password: true }).length, 0);
assert.equal(filesOf({ type: "file", name: "x", link: "https://store1.gofile.io/download/web/x/x" }).length, 1);
assert.equal(filesOf(null).length, 0);

// 2) gofile-hook.js: yalnızca contents yanıtlarını iletir
const posted = [];
const responses = {
  "https://api.gofile.io/contents/abc?page=1": { status: "ok", data: folder },
  "https://api.gofile.io/contents/zzz?page=1": { status: "error-notFound", data: {} },
  "https://api.gofile.io/accounts/1": { status: "ok", data: { id: 1 } },
};
const mkRes = (body) => ({ ok: true, clone() { return { json: async () => body }; } });
const h = {
  location: { origin: "https://gofile.io" },
  fetch: async (u) => mkRes(responses[u]),
  postMessage: (m) => posted.push(m),
};
h.window = h;
vm.runInNewContext(read("gofile-hook.js"), h);
for (const u of Object.keys(responses)) await h.window.fetch(u);
await new Promise((r) => setTimeout(r, 10));
assert.equal(posted.length, 1);
assert.equal(posted[0].source, "hizli-indir-gofile");
assert.equal(posted[0].data.id, "f1");

// 3) background.js: isGofileDownload
const bg = read("background.js");
const fn = bg.match(/function isGofileDownload[\s\S]*?\n}\n/)[0];
const isG = vm.runInNewContext(fn + "; isGofileDownload", { URL });
assert.ok(isG("https://store3.gofile.io/download/web/a/a.bin"));
assert.ok(!isG("https://gofile.io/d/abc"));
assert.ok(!isG("https://api.gofile.io/contents/abc"));
assert.ok(!isG("https://store3.gofile.io.evil.com/download/x"));
assert.ok(!isG("http://store3.gofile.io/download/x"));
console.log("gofile testleri: OK");
