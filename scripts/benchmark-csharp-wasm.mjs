import { execFileSync, spawn } from "node:child_process";
import { createServer } from "node:http";
import { accessSync, constants, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { performance } from "node:perf_hooks";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const compilerProject = join(root, "ConsoleApp1", "ConsoleApp1.csproj");
const spikeProject = join(root, "experiments", "csharp-wasm-spike", "CodeWasmSpike.csproj");
const runtimePath = join(root, "web-runtime", "code-vm-web.js");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-csharp-wasm-spike-"));
const inputDirectory = join(temporaryRoot, "input");
const publishDirectory = join(temporaryRoot, "publish");
const chrome = join(process.env.ProgramFiles ?? "C:\\Program Files", "Google", "Chrome", "Application", "chrome.exe");
const workloads = ["runtime_cpu", "verlet_kernel"];
const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

function statistics(samples) {
  const sorted = [...samples].sort((left, right) => left - right);
  const average = samples.reduce((sum, value) => sum + value, 0) / samples.length;
  const variance = samples.reduce((sum, value) => sum + (value - average) ** 2, 0) / samples.length;
  return {
    medianMs: sorted[Math.ceil(sorted.length * 0.5) - 1],
    p95Ms: sorted[Math.ceil(sorted.length * 0.95) - 1],
    coefficientOfVariation: Math.sqrt(variance) / average
  };
}

async function waitFor(read, timeout, description) {
  const deadline = Date.now() + timeout;
  let lastError;
  while (Date.now() < deadline) {
    try {
      const value = await read();
      if (value) return value;
    } catch (error) { lastError = error; }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${description}${lastError ? `: ${lastError.message}` : "."}`);
}

class DevTools {
  constructor(url) {
    this.socket = new WebSocket(url);
    this.nextId = 1;
    this.pending = new Map();
  }
  async open() {
    await new Promise((resolveOpen, rejectOpen) => {
      this.socket.addEventListener("open", resolveOpen, { once: true });
      this.socket.addEventListener("error", () => rejectOpen(new Error("DevTools connection failed.")), { once: true });
    });
    this.socket.addEventListener("message", event => {
      const message = JSON.parse(event.data);
      if (!message.id) return;
      const request = this.pending.get(message.id);
      if (!request) return;
      this.pending.delete(message.id);
      message.error ? request.reject(new Error(message.error.message)) : request.resolve(message.result);
    });
  }
  send(method, params = {}) {
    const id = this.nextId++;
    this.socket.send(JSON.stringify({ id, method, params }));
    return new Promise((resolveRequest, rejectRequest) => this.pending.set(id, { resolve: resolveRequest, reject: rejectRequest }));
  }
  close() { this.socket.close(); }
}

function enumerateFiles(directory) {
  const files = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) files.push(...enumerateFiles(path));
    else files.push(path);
  }
  return files;
}

function contentType(path) {
  return ({ ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".wasm": "application/wasm", ".json": "application/json" })[extname(path)] ?? "application/octet-stream";
}

async function runPublishedSpike(siteDirectory) {
  const server = createServer((request, response) => {
    const relative = decodeURIComponent(new URL(request.url, "http://localhost").pathname).replace(/^\/+/, "") || "index.html";
    const path = resolve(siteDirectory, relative);
    if (!path.startsWith(resolve(siteDirectory))) { response.writeHead(403).end(); return; }
    try {
      const content = readFileSync(path);
      response.writeHead(200, { "content-type": contentType(path), "cache-control": "no-store" });
      response.end(content);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise(resolveListen => server.listen(0, "127.0.0.1", resolveListen));
  const port = server.address().port;
  const profile = join(temporaryRoot, "chrome-profile");
  const child = spawn(chrome, ["--headless=new", "--disable-gpu", "--no-sandbox", "--remote-debugging-port=0", `--user-data-dir=${profile}`, "about:blank"], { stdio: "ignore", windowsHide: true });
  let tools;
  try {
    const debugPort = await waitFor(() => Number(readFileSync(join(profile, "DevToolsActivePort"), "utf8").split(/\r?\n/)[0]), 10_000, "Chrome DevTools port");
    const target = await waitFor(async () => {
      const targets = await (await fetch(`http://127.0.0.1:${debugPort}/json/list`)).json();
      return targets.find(candidate => candidate.type === "page");
    }, 10_000, "Chrome page target");
    tools = new DevTools(target.webSocketDebuggerUrl);
    await tools.open();
    await tools.send("Runtime.enable");
    await tools.send("Page.enable");
    await tools.send("Page.navigate", { url: `http://127.0.0.1:${port}/` });
    return await waitFor(async () => {
      const evaluated = await tools.send("Runtime.evaluate", {
        expression: "document.querySelector('#benchmark-result')?.textContent ?? ''",
        returnByValue: true
      });
      const text = evaluated.result?.value;
      if (!text || text === "running" || text === "loading") return null;
      const value = JSON.parse(text);
      if (value.error) throw new Error(value.error);
      return value;
    }, 180_000, "C# AOT Wasm benchmark results");
  } finally {
    tools?.close();
    child.kill();
    server.close();
  }
}

try {
  accessSync(chrome, constants.X_OK);
  mkdirSync(inputDirectory, { recursive: true });
  const runtimeSource = readFileSync(runtimePath, "utf8");
  const runtimeModule = await import(`data:text/javascript;base64,${Buffer.from(runtimeSource).toString("base64")}`);
  const jsResults = [];
  for (const workload of workloads) {
    const bytecodePath = join(inputDirectory, `${workload}.bytecode`);
    execFileSync("dotnet", ["run", "--project", compilerProject, "-c", "Release", "--no-build", "--", "--target", "vm-web", "--compile-only", "-o", bytecodePath, join(root, "benchmarks", `${workload}.code`)], { cwd: root, stdio: "ignore" });
    const bytecode = new Uint8Array(readFileSync(bytecodePath));
    const execute = () => new runtimeModule.WebVm(bytecode, { output: () => {} }).run();
    for (let run = 0; run < 5; run++) execute();
    const samples = [];
    for (let run = 0; run < 20; run++) { const started = performance.now(); execute(); samples.push(performance.now() - started); }
    jsResults.push({ workload, ...statistics(samples) });
  }

  const buildStarted = performance.now();
  execFileSync("dotnet", ["publish", spikeProject, "-c", "Release", "-o", publishDirectory, `-p:BenchmarkInputDir=${inputDirectory}`, "-p:RunAOTCompilation=true"], { cwd: root, stdio: "inherit" });
  const aotBuildMs = performance.now() - buildStarted;
  const siteDirectory = join(publishDirectory, "AppBundle");
  const files = enumerateFiles(siteDirectory);
  const payloadBytes = files.reduce((sum, path) => sum + statSync(path).size, 0);
  const gzipPayloadBytes = files.reduce((sum, path) => sum + gzipSync(readFileSync(path)).length, 0);
  const wasmReport = await runPublishedSpike(siteDirectory);
  const comparisons = wasmReport.results.map(wasm => {
    const js = jsResults.find(result => result.workload === wasm.workload);
    return { workload: wasm.workload, jsMedianMs: js.medianMs, wasmMedianMs: wasm.medianMs, speedup: js.medianMs / wasm.medianMs };
  });
  const geometricMeanSpeedup = Math.exp(comparisons.reduce((sum, value) => sum + Math.log(value.speedup), 0) / comparisons.length);
  const report = { aotBuildMs, startupMs: wasmReport.startupMs, payloadBytes, gzipPayloadBytes, managedMemoryBytes: wasmReport.managedMemoryBytes, comparisons, geometricMeanSpeedup, clearsTwoTimesGate: geometricMeanSpeedup >= 2 };
  console.table(comparisons);
  console.log(JSON.stringify(report, null, 2));
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
