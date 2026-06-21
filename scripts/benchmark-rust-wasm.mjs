import { execFileSync, spawn } from "node:child_process";
import { createServer } from "node:http";
import { accessSync, constants, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const compilerProject = join(root, "ConsoleApp1", "ConsoleApp1.csproj");
const runtimePath = join(root, "web-runtime", "code-vm-web.js");
const wasmCrate = join(root, "runtime-wasm");
const wasmPath = join(wasmCrate, "target", "wasm32-unknown-unknown", "release", "code_runtime_wasm.wasm");
const chrome = join(process.env.ProgramFiles ?? "C:\\Program Files", "Google", "Chrome", "Application", "chrome.exe");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-rust-wasm-benchmark-"));
const workloads = ["runtime_cpu", "verlet_kernel", "ball_regression"];
const warmupRuns = 5;
const sampleRuns = 20;
const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

function contentType(path) {
  return ({ ".html": "text/html", ".js": "text/javascript", ".wasm": "application/wasm", ".bytecode": "application/octet-stream" })[extname(path)] ?? "application/octet-stream";
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
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      message.error ? pending.reject(new Error(message.error.message)) : pending.resolve(message.result);
    });
  }
  send(method, params = {}) {
    const id = this.nextId++;
    this.socket.send(JSON.stringify({ id, method, params }));
    return new Promise((resolveRequest, rejectRequest) => this.pending.set(id, { resolve: resolveRequest, reject: rejectRequest }));
  }
  close() { this.socket.close(); }
}

function workerSource() {
  const runtime = readFileSync(runtimePath, "utf8").replace(/^\s*export\s+/gm, "");
  return `${runtime}

function statistics(samples) {
  const sorted = [...samples].sort((left, right) => left - right);
  const average = samples.reduce((sum, value) => sum + value, 0) / samples.length;
  const variance = samples.reduce((sum, value) => sum + ((value - average) ** 2), 0) / samples.length;
  const medianMs = sorted[Math.ceil(sorted.length * 0.5) - 1];
  return {
    medianMs,
    p95Ms: sorted[Math.ceil(sorted.length * 0.95) - 1],
    throughput: 1000 / medianMs,
    coefficientOfVariation: Math.sqrt(variance) / average
  };
}

function measure(execute, warmups, runs) {
  for (let index = 0; index < warmups; index += 1) execute();
  const samples = [];
  for (let index = 0; index < runs; index += 1) {
    const startedAt = performance.now();
    execute();
    samples.push(performance.now() - startedAt);
  }
  return statistics(samples);
}

self.onmessage = async event => {
  try {
    const { wasmBytes, artifacts, warmups, runs } = event.data;
    const wasm = await WebAssembly.instantiate(wasmBytes, { code_host: {
      call: () => 1,
      output: () => {},
      unix_milliseconds: () => Date.now(),
      monotonic_milliseconds: () => performance.now()
    } });
    const exports = wasm.instance.exports;
    if (exports.code_value_size() !== 16) throw new Error("Rust value layout is not 16 bytes.");
    const results = [];
    for (const artifact of artifacts) {
      const bytecode = new Uint8Array(artifact.bytecode);
      const jsExecute = () => new WebVm(bytecode, { output: () => {} }).run();
      const js = measure(jsExecute, warmups, runs);

      const pointer = exports.code_alloc(bytecode.byteLength);
      new Uint8Array(exports.memory.buffer, pointer, bytecode.byteLength).set(bytecode);
      const wasmExecute = () => {
        const status = exports.code_run(pointer, bytecode.byteLength);
        if (status !== 0) throw new Error(\`Rust VM status \${status} for '\${artifact.name}'.\`);
      };
      const rustWasm = measure(wasmExecute, warmups, runs);
      const output = exports.code_last_output_number();
      exports.code_dealloc(pointer, bytecode.byteLength);
      if (output !== 1) throw new Error(\`Rust VM produced \${output} for '\${artifact.name}', expected 1.\`);
      results.push({ name: artifact.name, js, rustWasm, speedup: js.medianMs / rustWasm.medianMs });
    }
    self.postMessage({ results });
  } catch (error) {
    self.postMessage({ error: error instanceof Error ? error.stack ?? error.message : String(error) });
  }
};`;
}

function pageSource() {
  return `<!doctype html><meta charset="utf-8"><pre id="result">running</pre><script>
  (async () => {
    try {
      const worker = new Worker("worker.js");
      const wasmBytes = await (await fetch("runtime.wasm")).arrayBuffer();
      const artifacts = await Promise.all(${JSON.stringify(workloads)}.map(async name => ({ name, bytecode: await (await fetch(name + ".bytecode")).arrayBuffer() })));
      worker.onmessage = event => { document.querySelector("#result").textContent = JSON.stringify(event.data); };
      worker.onerror = event => { document.querySelector("#result").textContent = JSON.stringify({ error: event.message }); };
      worker.postMessage({ wasmBytes, artifacts, warmups: ${warmupRuns}, runs: ${sampleRuns} }, [wasmBytes, ...artifacts.map(value => value.bytecode)]);
    } catch (error) {
      document.querySelector("#result").textContent = JSON.stringify({ error: error.stack ?? error.message ?? String(error) });
    }
  })();
  </script>`;
}

async function runChrome() {
  const server = createServer((request, response) => {
    const relative = decodeURIComponent(new URL(request.url, "http://localhost").pathname).replace(/^\/+/, "") || "index.html";
    const path = resolve(temporaryRoot, relative);
    if (!path.startsWith(resolve(temporaryRoot))) { response.writeHead(403).end(); return; }
    try {
      const bytes = readFileSync(path);
      response.writeHead(200, { "content-type": contentType(path), "cache-control": "no-store" });
      response.end(bytes);
    } catch { response.writeHead(404).end(); }
  });
  await new Promise(resolveListen => server.listen(0, "127.0.0.1", resolveListen));
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
    await tools.send("Page.navigate", { url: `http://127.0.0.1:${server.address().port}/` });
    return await waitFor(async () => {
      const evaluated = await tools.send("Runtime.evaluate", { expression: "document.querySelector('#result')?.textContent ?? ''", returnByValue: true });
      const text = evaluated.result?.value;
      if (!text || text === "running") return null;
      const result = JSON.parse(text);
      if (result.error) throw new Error(result.error);
      return result;
    }, 180_000, "Rust/Wasm benchmark results");
  } finally {
    tools?.close();
    if (!child.killed) child.kill();
    await Promise.race([
      new Promise(resolveExit => child.once("exit", resolveExit)),
      delay(2_000)
    ]);
    await new Promise(resolveClose => server.close(resolveClose));
  }
}

try {
  accessSync(chrome, constants.X_OK);
  execFileSync("cargo", ["build", "--release", "--target", "wasm32-unknown-unknown", "--locked"], { cwd: wasmCrate, stdio: "inherit" });
  for (const workload of workloads) {
    execFileSync("dotnet", ["run", "--project", compilerProject, "-c", "Release", "--no-build", "--", "--target", "vm-web", "--compile-only", "-o", join(temporaryRoot, `${workload}.bytecode`), join(root, "benchmarks", `${workload}.code`)], { cwd: root, stdio: "ignore" });
  }
  writeFileSync(join(temporaryRoot, "runtime.wasm"), readFileSync(wasmPath));
  writeFileSync(join(temporaryRoot, "worker.js"), workerSource());
  writeFileSync(join(temporaryRoot, "index.html"), pageSource());

  const report = await runChrome();
  const geometricMeanSpeedup = Math.exp(report.results.reduce((sum, result) => sum + Math.log(result.speedup), 0) / report.results.length);
  const ball = report.results.find(result => result.name === "ball_regression");
  const stable = report.results.every(result => result.js.coefficientOfVariation <= 0.15 && result.rustWasm.coefficientOfVariation <= 0.15);
  const noRegression = report.results.every(result => result.speedup >= 0.9);
  const passed = geometricMeanSpeedup >= 1.5 && ball.speedup >= 1.5 && stable && noRegression;
  const finalReport = { warmupRuns, sampleRuns, results: report.results, geometricMeanSpeedup, gates: { geometricMean: geometricMeanSpeedup >= 1.5, ballRegression: ball.speedup >= 1.5, stable, noRegression, passed } };
  console.table(report.results.map(result => ({ workload: result.name, jsMedianMs: result.js.medianMs, wasmMedianMs: result.rustWasm.medianMs, speedup: result.speedup, jsCv: result.js.coefficientOfVariation, wasmCv: result.rustWasm.coefficientOfVariation })));
  console.log(JSON.stringify(finalReport, null, 2));
  if (!passed) process.exitCode = 2;
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
}
