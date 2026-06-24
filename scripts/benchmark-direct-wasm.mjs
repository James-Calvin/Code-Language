import { execFileSync, spawn } from "node:child_process";
import { createServer } from "node:http";
import { accessSync, constants, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const compiler = join(root, "ConsoleApp1", "bin", "Release", "net9.0", "compiler.dll");
const wasmCrate = join(root, "runtime-wasm");
const runtimeWasm = join(wasmCrate, "target", "wasm32-unknown-unknown", "release", "code_runtime_wasm.wasm");
const chrome = join(process.env.ProgramFiles ?? "C:\\Program Files", "Google", "Chrome", "Application", "chrome.exe");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-direct-wasm-benchmark-"));
const workloads = ["runtime_cpu", "verlet_kernel", "ball_regression"];
const warmupRuns = 5;
const sampleRuns = 20;
const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

function contentType(path) {
  return ({ ".html": "text/html", ".js": "text/javascript", ".wasm": "application/wasm", ".bytecode": "application/octet-stream" })[extname(path)] ?? "application/octet-stream";
}

function compileArtifact(name, backend, extension) {
  const output = join(temporaryRoot, `${name}.${extension}`);
  const argumentsList = [compiler, "--target", "vm-web", "--compile-only"];
  if (backend === "direct-wasm") argumentsList.push("--web-backend", backend);
  argumentsList.push("-o", output, join(root, "benchmarks", `${name}.code`));
  const startedAt = performance.now();
  execFileSync("dotnet", argumentsList, { cwd: root, stdio: "ignore" });
  return { path: output, compilationMs: performance.now() - startedAt };
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
  return `
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
  let batchSize = 1;
  while (batchSize < 4096) {
    const calibrationStarted = performance.now();
    for (let index = 0; index < batchSize; index += 1) execute();
    if (performance.now() - calibrationStarted >= 25) break;
    batchSize *= 2;
  }
  const samples = [];
  for (let index = 0; index < runs; index += 1) {
    const startedAt = performance.now();
    for (let batchIndex = 0; batchIndex < batchSize; batchIndex += 1) execute();
    samples.push((performance.now() - startedAt) / batchSize);
  }
  return { ...statistics(samples), batchSize };
}

self.onmessage = async event => {
  try {
    const { runtimeBytes, artifacts, warmups, runs } = event.data;
    let rustExports;
    let rustOutput = 0;
    const rustCompileStarted = performance.now();
    const rustModule = await WebAssembly.compile(runtimeBytes);
    const rustCompileMs = performance.now() - rustCompileStarted;
    const rustInstantiateStarted = performance.now();
    const rustInstance = await WebAssembly.instantiate(rustModule, { code_host: {
      call: () => 1,
      output: (_context, pointer) => { rustOutput = new DataView(rustExports.memory.buffer).getFloat64(pointer, true); },
      unix_milliseconds: () => Date.now(),
      monotonic_milliseconds: () => performance.now()
    } });
    rustExports = rustInstance.exports;
    const rustInstantiateMs = performance.now() - rustInstantiateStarted;
    const results = [];

    for (const artifact of artifacts) {
      const bytecode = new Uint8Array(artifact.bytecode);
      const pointer = rustExports.code_alloc(bytecode.byteLength);
      new Uint8Array(rustExports.memory.buffer, pointer, bytecode.byteLength).set(bytecode);
      const handle = rustExports.code_vm_create(pointer, bytecode.byteLength);
      rustExports.code_dealloc(pointer, bytecode.byteLength);
      if (handle <= 0) throw new Error(\`Rust VM creation failed with status \${handle} for '\${artifact.name}'.\`);
      const rustExecute = () => {
        const status = rustExports.code_vm_run(handle);
        if (status !== 0) throw new Error(\`Rust VM status \${status} for '\${artifact.name}'.\`);
      };
      const rustWasm = measure(rustExecute, warmups, runs);
      if (rustOutput !== 1) throw new Error(\`Rust VM produced \${rustOutput} for '\${artifact.name}', expected 1.\`);
      rustExports.code_vm_destroy(handle);

      let directOutput = 0;
      const directCompileStarted = performance.now();
      const directModule = await WebAssembly.compile(artifact.directWasm);
      const directCompileMs = performance.now() - directCompileStarted;
      const directInstantiateStarted = performance.now();
      const directInstance = await WebAssembly.instantiate(directModule, {
        code_host: {
          print_i32: value => { directOutput = value; },
          print_i64: value => { directOutput = Number(value); },
          print_f64: value => { directOutput = value; },
          print_string: () => {}
        },
        code_runtime: {
          string_from_utf8: () => 1,
          string_concat: () => 1,
          string_equal: (left, right) => left === right ? 1 : 0,
          string_from_i32: () => 1,
          string_from_i64: () => 1,
          string_from_f64: () => 1
        }
      });
      const directInstantiateMs = performance.now() - directInstantiateStarted;
      const directExecute = () => {
        const status = directInstance.exports.code_run();
        if (status !== 0) throw new Error(\`Direct Wasm status \${status} for '\${artifact.name}'.\`);
      };
      const directWasm = measure(directExecute, warmups, runs);
      if (directOutput !== 1) throw new Error(\`Direct Wasm produced \${directOutput} for '\${artifact.name}', expected 1.\`);
      results.push({
        name: artifact.name,
        rustWasm,
        directWasm,
        speedup: rustWasm.medianMs / directWasm.medianMs,
        startup: { directCompileMs, directInstantiateMs }
      });
    }
    self.postMessage({ results, runtimeStartup: { compileMs: rustCompileMs, instantiateMs: rustInstantiateMs } });
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
      const runtimeBytes = await (await fetch("runtime.wasm")).arrayBuffer();
      const artifacts = await Promise.all(${JSON.stringify(workloads)}.map(async name => ({
        name,
        bytecode: await (await fetch(name + ".bytecode")).arrayBuffer(),
        directWasm: await (await fetch(name + ".wasm")).arrayBuffer()
      })));
      worker.onmessage = event => { document.querySelector("#result").textContent = JSON.stringify(event.data); };
      worker.onerror = event => { document.querySelector("#result").textContent = JSON.stringify({ error: event.message }); };
      const transfers = [runtimeBytes, ...artifacts.flatMap(value => [value.bytecode, value.directWasm])];
      worker.postMessage({ runtimeBytes, artifacts, warmups: ${warmupRuns}, runs: ${sampleRuns} }, transfers);
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
    }, 180_000, "direct-Wasm benchmark results");
  } finally {
    tools?.close();
    if (!child.killed) child.kill();
    await Promise.race([new Promise(resolveExit => child.once("exit", resolveExit)), delay(2_000)]);
    await new Promise(resolveClose => server.close(resolveClose));
  }
}

try {
  accessSync(chrome, constants.X_OK);
  execFileSync("cargo", ["build", "--release", "--target", "wasm32-unknown-unknown", "--locked"], {
    cwd: wasmCrate,
    stdio: "inherit",
    env: { ...process.env, RUSTUP_TOOLCHAIN: process.env.RUSTUP_TOOLCHAIN ?? "1.83.0-x86_64-pc-windows-msvc" }
  });
  const compilerMetrics = [];
  for (const workload of workloads) {
    const bytecode = compileArtifact(workload, "wasm-vm", "bytecode");
    const direct = compileArtifact(workload, "direct-wasm", "wasm");
    compilerMetrics.push({
      name: workload,
      bytecodeCompilationMs: bytecode.compilationMs,
      directCompilationMs: direct.compilationMs,
      bytecodeBytes: readFileSync(bytecode.path).byteLength,
      directWasmBytes: readFileSync(direct.path).byteLength
    });
  }
  writeFileSync(join(temporaryRoot, "runtime.wasm"), readFileSync(runtimeWasm));
  writeFileSync(join(temporaryRoot, "worker.js"), workerSource());
  writeFileSync(join(temporaryRoot, "index.html"), pageSource());

  const report = await runChrome();
  const geometricMeanSpeedup = Math.exp(report.results.reduce((sum, result) => sum + Math.log(result.speedup), 0) / report.results.length);
  const ball = report.results.find(result => result.name === "ball_regression");
  const stable = report.results.every(result => result.rustWasm.coefficientOfVariation <= 0.15 && result.directWasm.coefficientOfVariation <= 0.15);
  const noRegression = report.results.every(result => result.speedup >= 0.9);
  const passed = geometricMeanSpeedup >= 2 && ball.speedup >= 2 && stable && noRegression;
  const finalReport = {
    warmupRuns,
    sampleRuns,
    results: report.results,
    compilerMetrics,
    runtimeStartup: report.runtimeStartup,
    geometricMeanSpeedup,
    gates: { geometricMean: geometricMeanSpeedup >= 2, ballRegression: ball.speedup >= 2, stable, noRegression, passed }
  };
  console.table(report.results.map(result => ({
    workload: result.name,
    rustMedianMs: result.rustWasm.medianMs,
    directMedianMs: result.directWasm.medianMs,
    speedup: result.speedup,
    rustCv: result.rustWasm.coefficientOfVariation,
    directCv: result.directWasm.coefficientOfVariation
  })));
  console.table(compilerMetrics);
  console.log(JSON.stringify(finalReport, null, 2));
  if (!passed) process.exitCode = 2;
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true, maxRetries: 10, retryDelay: 100 });
}
