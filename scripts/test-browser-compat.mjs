import { spawn, execFileSync } from "node:child_process";
import { createServer } from "node:http";
import { accessSync, constants, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { basename, dirname, extname, join, normalize, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "..");
const project = join(repositoryRoot, "ConsoleApp1", "ConsoleApp1.csproj");
const installedCompiler = process.env.CODE_COMPILER?.trim() || null;
const backend = process.env.CODE_BROWSER_COMPAT_BACKEND?.trim() || "direct-wasm";
const disableDirectWasmGarbageCollection = process.env.CODE_DIRECT_WASM_DISABLE_GC === "1";
const requestedBrowser = process.env.CODE_BROWSER?.trim().toLowerCase() || null;
const provider = process.env.CODE_BROWSER_COMPAT_PROVIDER?.trim() || "local";

if (disableDirectWasmGarbageCollection && backend !== "direct-wasm") {
  throw new Error("CODE_DIRECT_WASM_DISABLE_GC=1 requires CODE_BROWSER_COMPAT_BACKEND=direct-wasm.");
}

let keepOutput = false;
let explicitOutputRoot = null;
for (let index = 2; index < process.argv.length; index += 1) {
  const arg = process.argv[index];
  if (arg === "--keep") keepOutput = true;
  else if (arg === "--out") {
    if (index + 1 >= process.argv.length) throw new Error("Usage: node scripts/test-browser-compat.mjs [--out folder] [--keep]");
    explicitOutputRoot = resolve(process.argv[++index]);
    keepOutput = true;
  } else {
    throw new Error(`Unknown argument '${arg}'.`);
  }
}

const temporaryRoot = mkdtempSync(join(tmpdir(), "code-browser-compat-"));
const suiteRoot = explicitOutputRoot ?? join(temporaryRoot, "suite");
const sourcesRoot = join(temporaryRoot, "sources");
mkdirSync(suiteRoot, { recursive: true });
mkdirSync(sourcesRoot, { recursive: true });

const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

async function removeTemporaryRoot(path) {
  const retryableCodes = new Set(["EBUSY", "ENOTEMPTY", "EPERM"]);
  let lastError = null;
  for (let attempt = 1; attempt <= 8; attempt += 1) {
    try {
      rmSync(path, { recursive: true, force: true, maxRetries: 3, retryDelay: 150 });
      return true;
    } catch (error) {
      lastError = error;
      if (!retryableCodes.has(error?.code) || attempt === 8) break;
      await delay(250 * attempt);
    }
  }

  console.warn(`Could not remove browser compatibility temp directory '${path}': ${lastError?.message ?? lastError}`);
  console.warn("The compatibility checks already completed; leaving the temp directory for later cleanup.");
  return false;
}

function executableExists(path) {
  try {
    accessSync(path, constants.X_OK);
    return true;
  } catch {
    return false;
  }
}

function browserCandidates() {
  const candidates = [];
  const add = (name, path) => candidates.push({ name, path, engine: "chromium-devtools" });
  if (process.env.CODE_BROWSER_PATH) add(process.env.CODE_BROWSER_NAME || basename(process.env.CODE_BROWSER_PATH), process.env.CODE_BROWSER_PATH);

  const programFiles = process.env.ProgramFiles ?? "C:\\Program Files";
  const programFilesX86 = process.env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)";
  add("Chrome", join(programFiles, "Google", "Chrome", "Application", "chrome.exe"));
  add("Edge", join(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"));
  add("Edge", join(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"));
  add("Chromium", "/usr/bin/chromium");
  add("Chromium", "/usr/bin/chromium-browser");
  add("Chrome", "/usr/bin/google-chrome");
  add("Chrome", "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
  add("Edge", "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
  return candidates.filter((candidate, index, all) =>
    executableExists(candidate.path) &&
    (!requestedBrowser || candidate.name.toLowerCase() === requestedBrowser) &&
    all.findIndex(other => other.name === candidate.name && other.path === candidate.path) === index);
}

function writeSource(name, text) {
  const path = join(sourcesRoot, `${name}.code`);
  writeFileSync(path, text.replace(/\n+$/, "\n"));
  return path;
}

const workloads = [
  {
    name: "minimal-lifecycle",
    source: writeSource("minimal-lifecycle", `
integer frames = 0;

function start() {
}

function update() {
  frames += 1;
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
}

function drawHud() {
  Draw.text("frames {frames}", 8, 8, 14, "left", "top", Colors.rgb(255, 255, 255));
}
`),
    minUpdates: 20
  },
  {
    name: "drawing-input",
    source: writeSource("drawing-input", `
real x = 320.;
real y = 180.;

function start() {
}

function update() {
  if Input.keyIsDown(37) then x -= 2.;
  if Input.keyIsDown(39) then x += 2.;
  if Input.pointerIsDown() then {
    x = Input.pointerWorldX();
    y = Input.pointerWorldY();
  }
}

function draw() {
  Draw.clearScreen(Colors.rgb(8, 12, 20));
  Draw.rectangleOutline(Viewport.safeLeft(), Viewport.safeTop(), Viewport.safeWidth(), Viewport.safeHeight(), 1, Colors.rgb(255, 255, 255));
  Draw.line(Viewport.safeLeft(), Viewport.safeTop(), Viewport.safeRight(), Viewport.safeBottom(), Colors.rgb(80, 160, 255));
  Draw.circle(x, y, 18, Colors.rgb(0, 128, 233));
}

function drawHud() {
  Draw.text("pointer {Input.pointerScreenX()}, {Input.pointerScreenY()}", 8, 8, 13, "left", "top", Colors.rgb(255, 255, 255));
}
`),
    minUpdates: 20
  },
  {
    name: "collection-object-stress",
    source: writeSource("collection-object-stress", `
array<Mover> movers;
integer tick = 0;

function start() {
  movers = new array<Mover>(96);
  foreach index in 96 then movers[index] = new Mover(index);
}

function update() {
  tick += 1;
  foreach mover in movers then mover.update();
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
  foreach mover in movers then mover.draw();
}

object Mover {
  real x;
  real y;
  real vx;

  constructor(integer index) {
    x = (index % 16) * 40 + 20;
    y = (index / 16) * 32 + 30;
    vx = 1. + ((index % 5) as real) / 4.;
  }

  function update() {
    x += vx;
    if x > Viewport.safeRight() then x = Viewport.safeLeft();
  }

  function draw() {
    Draw.rectangle(x, y, 10, 10, Colors.rgb(0, 180, 120));
  }
}
`),
    minUpdates: 40
  },
  {
    name: "allocation-growth",
    source: writeSource("allocation-growth", `
array<Node> nodes;
integer generation = 0;

function start() {
  nodes = new array<Node>(128);
}

function update() {
  generation += 1;
  foreach index in nodes.length then nodes[index] = new Node(index, generation);
}

function draw() {
  Draw.clearScreen(Colors.rgb(12, 8, 18));
  foreach node in nodes then node.draw();
}

object Node {
  real x;
  real y;
  integer age;

  constructor(integer index, integer generationValue) {
    x = (index % 32) * 20;
    y = (index / 32) * 28 + 20;
    age = generationValue;
  }

  function draw() {
    Draw.circle(x, y, 3, Colors.rgb(220, 180, 60));
  }
}
`),
    minUpdates: 50
  },
  {
    name: "performance-dashboard",
    source: join(repositoryRoot, "ConsoleApp1", "examples", "performance_dashboard.code"),
    minUpdates: 40
  }
];

function compileWorkload(workload) {
  const outputDirectory = join(suiteRoot, workload.name);
  const args = [workload.source, "-o", outputDirectory, "--web-backend", backend];
  if (disableDirectWasmGarbageCollection) args.push("--disable-garbage-collection");
  if (installedCompiler) execFileSync(installedCompiler, args, { cwd: repositoryRoot, stdio: "pipe" });
  else {
    execFileSync("dotnet", [
      "run", "--project", project, "-c", "Release", "--no-build", "--",
      ...args
    ], { cwd: repositoryRoot, stdio: "pipe" });
  }
  return outputDirectory;
}

function contentType(path) {
  return {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".wasm": "application/wasm",
    ".json": "application/json; charset=utf-8",
    ".svg": "image/svg+xml",
    ".wav": "audio/wav"
  }[extname(path).toLowerCase()] ?? "application/octet-stream";
}

async function startStaticServer(root) {
  const fullRoot = resolve(root);
  const server = createServer((request, response) => {
    try {
      const url = new URL(request.url ?? "/", "http://127.0.0.1");
      if (url.pathname === "/favicon.ico") {
        response.writeHead(204).end();
        return;
      }
      let target = resolve(fullRoot, `.${decodeURIComponent(url.pathname)}`);
      if (statSync(target, { throwIfNoEntry: false })?.isDirectory()) target = join(target, "index.html");
      const normalizedRoot = fullRoot.endsWith(sep) ? fullRoot : fullRoot + sep;
      if (target !== fullRoot && !target.startsWith(normalizedRoot)) {
        response.writeHead(403).end("Forbidden");
        return;
      }
      if (!existsSync(target)) {
        response.writeHead(404).end("Not found");
        return;
      }
      response.writeHead(200, { "Content-Type": contentType(target) });
      response.end(readFileSync(target));
    } catch (error) {
      response.writeHead(500).end(error?.message ?? String(error));
    }
  });
  await new Promise(resolveListen => server.listen(0, "127.0.0.1", resolveListen));
  const address = server.address();
  return { server, baseUrl: `http://127.0.0.1:${address.port}` };
}

async function waitFor(getValue, timeoutMilliseconds, description) {
  const deadline = Date.now() + timeoutMilliseconds;
  let lastError = null;
  while (Date.now() < deadline) {
    try {
      const value = await getValue();
      if (value) return value;
    } catch (error) {
      lastError = error;
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${description}${lastError ? `: ${lastError.message}` : "."}`);
}

class DevToolsConnection {
  constructor(url) {
    this.socket = new WebSocket(url);
    this.nextId = 1;
    this.pending = new Map();
    this.listeners = [];
  }

  async open() {
    await new Promise((resolveOpen, rejectOpen) => {
      this.socket.addEventListener("open", resolveOpen, { once: true });
      this.socket.addEventListener("error", () => rejectOpen(new Error("DevTools WebSocket failed to open.")), { once: true });
    });
    this.socket.addEventListener("message", event => this.onMessage(JSON.parse(event.data)));
  }

  onMessage(message) {
    if (message.id) {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(message.error.message));
      else pending.resolve(message.result);
      return;
    }
    for (const listener of this.listeners) listener(message);
  }

  onEvent(listener) { this.listeners.push(listener); }

  send(method, params = {}, sessionId = undefined) {
    const id = this.nextId++;
    const message = { id, method, params };
    if (sessionId) message.sessionId = sessionId;
    this.socket.send(JSON.stringify(message));
    return new Promise((resolveRequest, rejectRequest) => this.pending.set(id, { resolve: resolveRequest, reject: rejectRequest }));
  }

  close() { this.socket.close(); }
}

async function runAutomatedBrowser(browser, workload, pageUrl) {
  const profileDirectory = join(temporaryRoot, `${browser.name.toLowerCase()}-${workload.name}-profile`);
  const child = spawn(browser.path, [
    "--headless=new", "--disable-gpu", "--no-sandbox", "--remote-debugging-port=0",
    `--user-data-dir=${profileDirectory}`, "about:blank"
  ], { stdio: "ignore", windowsHide: true });
  const childExit = new Promise(resolveExit => child.once("exit", resolveExit));
  let connection = null;
  const diagnostics = [];
  try {
    const activePort = await waitFor(() => {
      const lines = readFileSync(join(profileDirectory, "DevToolsActivePort"), "utf8").trim().split(/\r?\n/);
      return lines.length >= 1 ? Number(lines[0]) : 0;
    }, 10_000, `${browser.name} DevTools port`);
    const target = await waitFor(async () => {
      const response = await fetch(`http://127.0.0.1:${activePort}/json/list`);
      const targets = await response.json();
      return targets.find(candidate => candidate.type === "page");
    }, 10_000, `${browser.name} page target`);

    connection = new DevToolsConnection(target.webSocketDebuggerUrl);
    await connection.open();
    connection.onEvent(message => {
      if (message.method === "Runtime.exceptionThrown") {
        diagnostics.push(`exception: ${message.params.exceptionDetails?.text ?? "unknown"}`);
      } else if (message.method === "Runtime.consoleAPICalled" && ["error", "assert"].includes(message.params.type)) {
        const values = message.params.args?.map(argument => argument.value ?? argument.description).join(" ");
        diagnostics.push(`console.${message.params.type}: ${values}`);
      } else if (message.method === "Log.entryAdded" && message.params.entry?.level === "error") {
        diagnostics.push(`log: ${message.params.entry.text}`);
      } else if (message.method === "Target.attachedToTarget") {
        const sessionId = message.params.sessionId;
        connection.send("Runtime.enable", {}, sessionId).catch(() => {});
        connection.send("Runtime.runIfWaitingForDebugger", {}, sessionId).catch(() => {});
      }
    });
    await connection.send("Runtime.enable");
    await connection.send("Log.enable");
    await connection.send("Page.enable");
    await connection.send("Target.setAutoAttach", {
      autoAttach: true, waitForDebuggerOnStart: true, flatten: true
    });
    await connection.send("Page.navigate", { url: pageUrl });

    const state = await waitFor(async () => {
      const result = await connection.send("Runtime.evaluate", {
        expression: "({ state: document.body?.dataset?.codeRuntime ?? '', error: document.querySelector('pre')?.textContent ?? '' })",
        returnByValue: true
      });
      const value = result.result?.value;
      return value?.state === "frame" || value?.state === "fatal" ? value : null;
    }, 20_000, `${browser.name} ${workload.name} frame state`);
    if (state.state !== "frame") throw new Error(`${browser.name} ${workload.name} failed: ${state.error || diagnostics.join("\n") || "no diagnostic"}`);

    await connection.send("Runtime.evaluate", {
      expression: "(() => { const controller = CodeRuntime.controller; window.__codeCompatProbe = { updates: 0, draws: 0, dropped: 0 }; const apply = controller.applyDiagnostics.bind(controller); controller.applyDiagnostics = value => { __codeCompatProbe.updates += value.updateSteps ?? 0; __codeCompatProbe.draws += 1; __codeCompatProbe.dropped += value.droppedUpdateSteps ?? 0; apply(value); }; window.dispatchEvent(new KeyboardEvent('keydown', { keyCode: 39, which: 39, bubbles: true })); window.dispatchEvent(new KeyboardEvent('keyup', { keyCode: 39, which: 39, bubbles: true })); })()"
    });
    const sustained = await waitFor(async () => {
      const result = await connection.send("Runtime.evaluate", {
        expression: "window.__codeCompatProbe",
        returnByValue: true
      });
      const value = result.result?.value;
      return value?.updates >= workload.minUpdates ? value : null;
    }, 25_000, `${browser.name} ${workload.name} sustained updates`);
    if (sustained.draws < 1) throw new Error(`${browser.name} ${workload.name} completed no draws.`);

    const profileResult = await connection.send("Runtime.evaluate", {
      expression: "(async () => { await CodeRuntime.profile.start(); await new Promise(resolve => setTimeout(resolve, 150)); const report = await CodeRuntime.profile.report(); await CodeRuntime.profile.stop(); return report; })()",
      awaitPromise: true,
      returnByValue: true
    });
    const profile = profileResult.result?.value;
    if (backend === "direct-wasm") {
      if (profile?.backend !== "direct-wasm" || profile?.instructionCount !== 0 || !Array.isArray(profile?.opcodes) || profile.opcodes.length !== 0) {
        throw new Error(`${browser.name} ${workload.name} did not report direct-Wasm profile shape.`);
      }
      if (disableDirectWasmGarbageCollection && profile?.garbageCollectionDisabled !== true) {
        throw new Error(`${browser.name} ${workload.name} did not report disabled direct-Wasm GC.`);
      }
    } else if (profile?.runtime !== "rust-wasm" || !(profile?.instructionCount > 0)) {
      throw new Error(`${browser.name} ${workload.name} did not report Rust/Wasm VM profile shape.`);
    }
    if (diagnostics.length > 0) throw new Error(diagnostics.join("\n"));

    return {
      status: "passed",
      updates: sustained.updates,
      draws: sustained.draws,
      droppedUpdateSteps: sustained.dropped,
      profile
    };
  } finally {
    connection?.close();
    if (child.exitCode === null && child.signalCode === null) child.kill();
    await Promise.race([childExit, delay(5_000)]);
  }
}

function writeManualReportPage(workloadEntries) {
  const html = `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Code Browser Compatibility Manual Report</title>
  <style>
    body { font-family: system-ui, sans-serif; margin: 2rem; line-height: 1.45; }
    code, textarea { font-family: ui-monospace, Consolas, monospace; }
    textarea { width: 100%; min-height: 14rem; }
    li { margin: .35rem 0; }
  </style>
</head>
<body>
  <h1>Code Browser Compatibility Manual Report</h1>
  <p>Open each workload on the target browser/device, verify it reaches a running frame, then download the report JSON.</p>
  <ul>
    ${workloadEntries.map(entry => `<li><a href="${entry.name}/index.html?code-profile=1">${entry.name}</a></li>`).join("\n    ")}
  </ul>
  <p><button id="pass">Mark Pass</button> <button id="fail">Mark Fail</button> <button id="download">Download JSON</button></p>
  <textarea id="notes" placeholder="Device/browser notes, visible errors, frame behavior, input behavior"></textarea>
  <script>
    const report = {
      schemaVersion: 1,
      kind: "manual-mobile-browser-report",
      createdAt: new Date().toISOString(),
      backend: ${JSON.stringify(backend)},
      directWasmGarbageCollectionDisabled: ${JSON.stringify(disableDirectWasmGarbageCollection)},
      userAgent: navigator.userAgent,
      platform: navigator.platform,
      viewport: { width: innerWidth, height: innerHeight, devicePixelRatio },
      status: "unmarked",
      workloads: ${JSON.stringify(workloadEntries.map(entry => entry.name))}
    };
    const notes = document.getElementById("notes");
    document.getElementById("pass").onclick = () => { report.status = "passed"; };
    document.getElementById("fail").onclick = () => { report.status = "failed"; };
    document.getElementById("download").onclick = () => {
      report.notes = notes.value;
      const blob = new Blob([JSON.stringify(report, null, 2)], { type: "application/json" });
      const link = document.createElement("a");
      link.href = URL.createObjectURL(blob);
      link.download = "code-browser-compat-manual-report.json";
      link.click();
    };
  </script>
</body>
</html>`;
  writeFileSync(join(suiteRoot, "mobile-report.html"), html);
}

const report = {
  schemaVersion: 1,
  kind: "code-browser-compatibility",
  createdAt: new Date().toISOString(),
  backend,
  directWasmGarbageCollectionDisabled: disableDirectWasmGarbageCollection,
  provider,
  suiteRoot,
  results: []
};
const reportPath = process.env.CODE_BROWSER_COMPAT_REPORT
  ? resolve(process.env.CODE_BROWSER_COMPAT_REPORT)
  : join(suiteRoot, "browser-compat-report.json");

function writeReport() {
  writeFileSync(reportPath, JSON.stringify(report, null, 2));
}

let server = null;
try {
  if (provider !== "local") {
    report.providerNote = "Provider hooks are reserved in V1; run with CODE_BROWSER_COMPAT_PROVIDER=local for built-in desktop automation.";
  }

  const workloadEntries = workloads.map(workload => ({
    name: workload.name,
    outputDirectory: compileWorkload(workload),
    minUpdates: workload.minUpdates
  }));
  writeManualReportPage(workloadEntries);

  const serverInfo = await startStaticServer(suiteRoot);
  server = serverInfo.server;
  report.server = serverInfo.baseUrl;

  const browsers = browserCandidates();
  if (browsers.length === 0) {
    const message = "No supported Chromium-family desktop browser found. Set CODE_BROWSER_PATH to a Chrome/Edge/Chromium executable or use mobile-report.html manually.";
    report.results.push({ status: "skipped", reason: message });
    writeReport();
    keepOutput = true;
    throw new Error(message);
  }

  for (const workload of workloadEntries) {
    const pageUrl = `${serverInfo.baseUrl}/${encodeURIComponent(workload.name)}/index.html`;
    for (const browser of browsers) {
      const result = { browser: browser.name, browserPath: browser.path, workload: workload.name, status: "failed" };
      try {
        Object.assign(result, await runAutomatedBrowser(browser, workload, pageUrl));
        console.log(`[PASS] browser-compat ${backend} ${browser.name} ${workload.name} (${result.updates} updates, ${result.draws} draws)`);
      } catch (error) {
        result.error = error?.message ?? String(error);
        console.error(`[FAIL] browser-compat ${backend} ${browser.name} ${workload.name}: ${result.error}`);
      }
      report.results.push(result);
    }
  }

  writeReport();
  console.log(`Browser compatibility report: ${reportPath}`);
  console.log(`Manual/mobile report page: ${join(suiteRoot, "mobile-report.html")}`);

  const failures = report.results.filter(result => result.status !== "passed");
  if (failures.length > 0) {
    keepOutput = true;
    throw new Error(`${failures.length} browser compatibility checks failed.`);
  }
} finally {
  await new Promise(resolveClose => server?.close(resolveClose) ?? resolveClose());
  if (!keepOutput) await removeTemporaryRoot(temporaryRoot);
  else console.log(`Kept browser compatibility suite at ${suiteRoot}`);
}
