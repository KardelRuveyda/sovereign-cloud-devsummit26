// Leak Hunt Lab server. Runs the REAL solution on this machine and streams everything to the browser IDE:
//  - edits LeakHunt.App\Program.cs on disk (the one-line change)
//  - runs `dotnet build` and `dotnet run` for real
//  - runs the real EgressTrafficMonitor and egress-connections.ps1
// No dependencies. Start with lab\Start_Lab.cmd.
const http = require("http");
const fs = require("fs");
const path = require("path");
const net = require("net");
const { spawn, execFileSync } = require("child_process");

const ROOT = path.join(__dirname, "..");
const LH = ROOT;
const APP_DIR = path.join(LH, "LeakHunt.App");
const PROGRAM = path.join(APP_DIR, "Program.cs");
const CONN_PS1 = path.join(LH, "egress-connections.ps1");
const PORT = +(process.env.LAB_PORT || 8770);

const find = (dir, name) => { const out = []; (function walk(d) { if (!fs.existsSync(d)) return; for (const e of fs.readdirSync(d, { withFileTypes: true })) { const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (e.name === name) out.push(p); } })(dir); return out[0]; };
let MON_EXE;
const locate = () => { MON_EXE = find(path.join(LH, "EgressTrafficMonitor", "bin", "Release"), "EgressTrafficMonitor.exe"); };

const state = { ready: false, running: false, building: false, online: true };
const clients = new Set();
const esc = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const scrub = (s) => typeof s !== "string" ? s : s.replace(new RegExp(esc(ROOT), "gi"), "<repo>").replace(new RegExp(esc(ROOT.replace(/\\/g, "/")), "gi"), "<repo>").replace(new RegExp(esc(require("os").homedir()), "gi"), "~").replace(new RegExp(esc(require("os").userInfo().username), "gi"), "user");
const send = (src, text, extra = {}) => { const msg = JSON.stringify({ src, text: scrub(text), ts: Date.now(), ...extra }); for (const c of clients) c.write(`data: ${msg}\n\n`); };

// ---------- the one-line change (real file edit) ----------
const LINES = { cloud: /IChatClient chat = Backend\.Cloud\(\);/, local: /IChatClient chat = await Backend\.LocalAsync\(\);/, edge: /IChatClient chat = Backend\.Edge\(\);/ };
function swapCode(mode) {
  const src = fs.readFileSync(PROGRAM, "utf8").replace(/^\uFEFF/, "");
  const out = src.split(/(\r?\n)/).map((ln) => {
    for (const [m, rx] of Object.entries(LINES)) if (rx.test(ln)) { const body = ln.replace(/^\s*(\/\/\s?)+/, ""); return m === mode ? body : "// " + body; }
    return ln;
  }).join("");
  if (out !== src) fs.writeFileSync(PROGRAM, out, "utf8");
}
const readMode = () => { const t = fs.readFileSync(PROGRAM, "utf8"); for (const [m, rx] of Object.entries(LINES)) { const ln = t.split(/\r?\n/).find((l) => rx.test(l)); if (ln && !/^\s*\/\//.test(ln)) return m; } return "none"; };

// ---------- file access for the editor ----------
const ALLOW = new Set([".cs", ".md", ".ps1", ".csproj", ".json", ".js", ".cmd"]);
const HIDE_DIR = new Set(["bin", "obj", ".vs", "Properties", "data", "img", "node_modules"]);
const HIDE_FILE = (n) => n === "lab.html" || n === "launchSettings.json" || /^appsettings.*\.json$/.test(n);
function tree() {
  const out = [];
  (function walk(d, rel) {
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      if (e.isDirectory() ? HIDE_DIR.has(e.name) : HIDE_FILE(e.name)) continue;
      const p = path.join(d, e.name), r = rel ? rel + "/" + e.name : e.name;
      if (e.isDirectory()) walk(p, r); else if (ALLOW.has(path.extname(e.name))) out.push(r);
    }
  })(LH, "");
  return out.sort();
}
const safeFile = (rel) => { const p = path.normalize(path.join(LH, rel)); if (!p.startsWith(LH + path.sep) || !ALLOW.has(path.extname(p)) || !fs.existsSync(p)) return null; return p; };

// ---------- processes ----------
let app, mon, conn;
const killProc = (p) => { try { if (p && p.exitCode === null) execFileSync("taskkill", ["/PID", String(p.pid), "/T", "/F"], { stdio: "ignore" }); } catch (_) {} };
const noise = (s) => /Logging before InitGoogleLogging|^[IWEF]\d{8} \d\d:\d\d:\d\d\.\d+ \d+ /m.test(s);

function startMonitor() {
  if (mon && mon.exitCode === null) return;
  locate();
  mon = spawn(MON_EXE, [], { cwd: path.dirname(MON_EXE), windowsHide: true });
  mon.stdout.on("data", (d) => send("monitor", d.toString()));
  mon.stderr.on("data", (d) => { const s = d.toString(); if (!noise(s)) send("monitor", s); });
}
function startConn() {
  if (conn && conn.exitCode === null) return;
  conn = spawn("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", CONN_PS1], { windowsHide: true });
  let buf = "";
  conn.stdout.on("data", (d) => { buf += d.toString(); let i; while ((i = buf.indexOf("\n")) >= 0) { const l = buf.slice(0, i).replace(/\r$/, ""); buf = buf.slice(i + 1); if (l.trim()) send("conn", l); } });
}
function stopApp() { state.ready = false; state.running = false; if (app) killProc(app); app = null; }

function build() {
  stopApp();
  if (state.building) return;
  state.building = true; send("state", "build-start");
  const t0 = Date.now();
  const p = spawn("dotnet", ["build", APP_DIR, "-c", "Release", "--nologo", "-v", "minimal"], { windowsHide: true, cwd: LH });
  const rd = (d) => d.toString().split(/\r?\n/).filter((l) => l.trim()).forEach((l) => send("build", l.length > 200 ? l.slice(0, 200) + " ..." : l));
  p.stdout.on("data", rd); p.stderr.on("data", rd);
  p.on("exit", (c) => { state.building = false; send("state", c === 0 ? "build-ok" : "build-fail", { sec: (Date.now() - t0) / 1000 }); });
}

function run(model) {
  stopApp();
  state.running = true; send("state", "run-start");
  app = spawn("dotnet", ["run", "--project", APP_DIR, "-c", "Release", "--no-build"], { windowsHide: true, cwd: LH, env: { ...process.env, CLOUD_ENDPOINT: process.env.CLOUD_ENDPOINT || "http://127.0.0.1:5099/v1", LH_MODEL: /^[\w.\-]+$/.test(model || "") ? model : "phi-3.5-mini" } });
  let acc = "";

  app.stdout.on("data", (d) => { const s = d.toString(); if (!s) return; acc += s; send("app", s); if (!state.ready && /Ask about the internal documents/.test(acc)) { state.ready = true; send("state", "ready"); } });
  app.stderr.on("data", (d) => { const s = d.toString(); if (!noise(s) && s.trim()) send("app", s); });
  app.on("exit", () => { state.ready = false; state.running = false; send("state", "stopped"); });
}

const WIFI_PS1 = path.join(__dirname, "wifi.ps1");
const WIFI_FLAG = path.join(__dirname, "data", "wifi_off.flag");
function wifi(action) {
  return new Promise((resolve) => {
    const p = spawn("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WIFI_PS1, "-Action", action], { windowsHide: true });
    let out = ""; p.stdout.on("data", (d) => (out += d)); p.on("exit", () => {
      const m = /wifi=(\S+)/.exec(out); const st = m ? m[1] : "error";
      try { if (st === "off") fs.writeFileSync(WIFI_FLAG, "1"); else if (st === "on" && fs.existsSync(WIFI_FLAG)) fs.unlinkSync(WIFI_FLAG); } catch (_) {}
      resolve(st);
    });
  });
}const BENCH_DIR = path.join(ROOT, "reports");
let bench = { running: false, alias: "", log: [] };
function benchList() {
  const out = [];
  try { for (const f of fs.readdirSync(BENCH_DIR)) if (f.endsWith(".json")) { const j = JSON.parse(fs.readFileSync(path.join(BENCH_DIR, f), "utf8")); out.push({ ...j.summary, rows: (j.rows || []).map((r) => ({ id: r.Id, kind: r.Kind, text: r.Text, pass: r.pass, hallucinated: r.hallucinated })) }); } } catch (_) {}
  return out;
}
function runBench(alias) {
  if (bench.running) return;
  stopApp(); bench = { running: true, alias, log: [] };
  const p = spawn("dotnet", ["run", "--project", path.join(LH, "LeakHunt.Bench"), "-c", "Release", "--", alias, BENCH_DIR], { windowsHide: true, cwd: LH });
  const add = (d) => d.toString().split(/\r?\n/).filter((l) => l.trim()).forEach((l) => { bench.log.push(l); if (bench.log.length > 60) bench.log.shift(); });
  p.stdout.on("data", add); p.stderr.on("data", add);
  p.on("exit", () => { bench.running = false; bench.log.push("finished"); });
}
const PRISTINE = path.join(__dirname, "data", "Program.orig.cs");
function restoreProgram() { try { if (fs.existsSync(PRISTINE)) { fs.copyFileSync(PRISTINE, PROGRAM); const n = new Date(); fs.utimesSync(PROGRAM, n, n); } } catch (_) {} }
function resetAll() {
  if (fs.existsSync(WIFI_FLAG)) wifi("on");
  stopApp(); killProc(mon); mon = null;
  restoreProgram(); swapCode("cloud");
  send("reset", "");
  setTimeout(startMonitor, 600);
}

// ---------- connectivity ----------
const probe = (host, port) => new Promise((res) => { const s = net.connect({ host, port, timeout: 1500 }); s.on("connect", () => { s.destroy(); res(true); }); s.on("error", () => res(false)); s.on("timeout", () => { s.destroy(); res(false); }); });
async function netLoop() { for (;;) { const r = await Promise.all([probe("1.1.1.1", 443), probe("8.8.8.8", 53), probe("login.microsoftonline.com", 443)]); const on = r.some(Boolean); if (on !== state.online) { state.online = on; send("net", on ? "online" : "offline", { online: on }); } await new Promise((r2) => setTimeout(r2, 1500)); } }

// ---------- http ----------
const MIME = { ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".png": "image/png", ".json": "application/json", ".css": "text/css" };
const body = (req) => new Promise((res) => { let b = ""; req.on("data", (d) => (b += d)); req.on("end", () => { try { res(JSON.parse(b || "{}")); } catch { res({}); } }); });
const json = (res, code, o) => { res.writeHead(code, { "Content-Type": "application/json", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" }); res.end(JSON.stringify(o)); };
// Local-only hardening: reject DNS rebinding (unexpected Host) and cross-site requests (foreign Origin or non-JSON POST).
const OK_HOSTS = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);
const OK_ORIGINS = new Set([`http://127.0.0.1:${PORT}`, `http://localhost:${PORT}`]);
function guard(req, res) {
  const bad = !OK_HOSTS.has(req.headers.host || "") || (req.headers.origin && !OK_ORIGINS.has(req.headers.origin)) || (req.method === "POST" && !/^application\/json\b/i.test(req.headers["content-type"] || ""));
  if (bad) { res.writeHead(403, { "Content-Type": "text/plain" }); res.end("forbidden"); }
  return !bad;
}
const server = http.createServer(async (req, res) => {
  if (!guard(req, res)) return;
  const u = new URL(req.url, "http://x");
  if (u.pathname === "/events") {
    res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache", Connection: "keep-alive" });
    res.write(`data: ${JSON.stringify({ src: "hello", state })}\n\n`);
    clients.add(res); req.on("close", () => clients.delete(res)); return;
  }
  if (u.pathname === "/api/bench" && req.method === "GET") return json(res, 200, { running: bench.running, alias: bench.alias, log: bench.log.slice(-8), models: benchList() });
    if (u.pathname === "/api/state") return json(res, 200, { ...state, mode: readMode() });
  if (u.pathname === "/api/tree") return json(res, 200, tree());
  if (u.pathname === "/api/file") { const p = safeFile(u.searchParams.get("p") || ""); if (!p) return json(res, 404, {}); return json(res, 200, { text: fs.readFileSync(p, "utf8").replace(/^\uFEFF/, "") }); }
  if (req.method === "POST") {
    const b = await body(req);
    if (u.pathname === "/api/edit") { if (!["cloud", "local"].includes(b.mode)) return json(res, 400, {}); stopApp(); swapCode(b.mode); send("code", "saved"); return json(res, 200, { mode: readMode() }); }
    if (u.pathname === "/api/save") { if (typeof b.text !== "string" || b.text.length > 20000 || !/Backend\./.test(b.text)) return json(res, 400, {}); stopApp(); fs.writeFileSync(PROGRAM, b.text.replace(/\r?\n/g, "\r\n"), "utf8"); send("code", "saved"); return json(res, 200, { mode: readMode() }); }
    if (u.pathname === "/api/build") { build(); return json(res, 202, {}); }
    if (u.pathname === "/api/run") { run(b.model); return json(res, 202, {}); }
    if (u.pathname === "/api/revert") { stopApp(); restoreProgram(); send("code", "reverted"); return json(res, 200, { mode: readMode() }); }
    if (u.pathname === "/api/bench") { if (req.method === "POST") { if (!/^[\w.\-]+$/.test(b.alias || "")) return json(res, 400, {}); runBench(b.alias); return json(res, 202, {}); } return json(res, 200, { running: bench.running, alias: bench.alias, log: bench.log.slice(-8), models: benchList() }); }
    if (u.pathname === "/api/wifi" && req.method === "POST") { if (!["on", "off", "status"].includes(b.action)) return json(res, 400, {}); return wifi(b.action).then((st) => json(res, 200, { wifi: st })); }
    if (u.pathname === "/api/stop") { stopApp(); send("state", "stopped"); return json(res, 200, {}); }
    if (u.pathname === "/api/ask") { if (app && state.ready && b.text) { app.stdin.write(String(b.text).replace(/[\r\n]+/g, " ") + "\n"); return json(res, 200, {}); } return json(res, 409, {}); }
    if (u.pathname === "/api/reset") { resetAll(); return json(res, 202, {}); }
  }
  if (u.pathname !== "/" && u.pathname !== "/lab.html") { res.writeHead(404); return res.end("not found"); }
  const fp = path.join(__dirname, "lab.html");
  res.writeHead(200, { "Content-Type": MIME[path.extname(fp)] || "application/octet-stream", "Cache-Control": "no-store" }); fs.createReadStream(fp).pipe(res);
});

function cleanup() { try { stopApp(); killProc(mon); killProc(conn); restoreProgram(); swapCode("cloud"); } catch (_) {} process.exit(0); }
process.on("SIGINT", cleanup); process.on("SIGTERM", cleanup);

locate();
if (!MON_EXE) { console.error("Build first: dotnet build LeakHunt\\EgressTrafficMonitor -c Release"); process.exit(1); }
restoreProgram(); swapCode("cloud");
process.on("SIGINT", () => { if (fs.existsSync(WIFI_FLAG)) wifi("on").then(() => process.exit(0)); else process.exit(0); });
process.on("SIGTERM", () => { if (fs.existsSync(WIFI_FLAG)) wifi("on").then(() => process.exit(0)); else process.exit(0); });
server.listen(PORT, "127.0.0.1", () => { if (fs.existsSync(WIFI_FLAG)) wifi("on"); console.log(`Leak Hunt Lab: http://127.0.0.1:${PORT}/`); startMonitor(); startConn(); netLoop(); });

