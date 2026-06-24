import { spawn, execFileSync } from "node:child_process";
import { accessSync, constants, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { pathToFileURL, fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "..");
const project = join(repositoryRoot, "ConsoleApp1", "ConsoleApp1.csproj");
const installedCompiler = process.env.CODE_COMPILER?.trim() || null;
const webBackend = process.env.CODE_WEB_BACKEND?.trim() || "wasm-vm";
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-worker-browser-"));
let workloads = [
  { name: "web-scene", source: join(repositoryRoot, "ConsoleApp1", "examples", "web_scene.code") },
  { name: "ball-130", source: join(repositoryRoot, "benchmarks", "ball_scene.code") },
  { name: "gravity-diagnostics", source: join(repositoryRoot, "benchmarks", "worker_diagnostics_gravity.code") }
];
const capacityCounts = (process.env.CODE_BALL_COUNTS ?? "").split(",").map(value => Number(value.trim())).filter(value => Number.isInteger(value) && value > 0);
if (capacityCounts.length > 0) {
  const template = readFileSync(join(repositoryRoot, "benchmarks", "ball_scene.code"), "utf8");
  workloads = capacityCounts.map(count => {
    const source = join(temporaryRoot, `ball-${count}.code`);
    writeFileSync(source, template.replace("constant integer SCENE_BALL_COUNT = 130;", `constant integer SCENE_BALL_COUNT = ${count};`).replace("130-ball worker regression", `${count}-ball worker benchmark`));
    return { name: `ball-${count}`, source };
  });
}

const programFiles = process.env.ProgramFiles ?? "C:\\Program Files";
const programFilesX86 = process.env["ProgramFiles(x86)"] ?? "C:\\Program Files (x86)";
const browserCandidates = [
  { name: "Chrome", path: join(programFiles, "Google", "Chrome", "Application", "chrome.exe") },
  { name: "Edge", path: join(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe") },
  { name: "Edge", path: join(programFiles, "Microsoft", "Edge", "Application", "msedge.exe") }
];

const delay = milliseconds => new Promise(resolveDelay => setTimeout(resolveDelay, milliseconds));

function exists(path) {
  try {
    accessSync(path, constants.X_OK);
    return true;
  } catch {
    return false;
  }
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

async function runBrowserSmoke(browser, pageUrl, workloadName) {
  const profileDirectory = join(temporaryRoot, `${browser.name.toLowerCase()}-${workloadName}-profile`);
  const child = spawn(browser.path, [
    "--headless=new", "--disable-gpu", "--no-sandbox", "--remote-debugging-port=0",
    `--user-data-dir=${profileDirectory}`, "about:blank"
  ], { stdio: "ignore", windowsHide: true });
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
      } else if (message.method === "Runtime.consoleAPICalled") {
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
    }, 15_000, `${browser.name} worker frame`);

    if (state.state !== "frame") {
      throw new Error(`${browser.name} worker failed: ${state.error || diagnostics.join("\n") || "no diagnostic"}`);
    }
    await connection.send("Runtime.evaluate", {
      expression: "(() => { const controller = CodeRuntime.controller; window.__codeWorkerProbe = { updates: 0, draws: 0, dropped: 0, updateWork: 0, samples: [] }; const apply = controller.applyDiagnostics.bind(controller); controller.applyDiagnostics = value => { __codeWorkerProbe.updates += value.updateSteps ?? 0; __codeWorkerProbe.draws += 1; __codeWorkerProbe.dropped += value.droppedUpdateSteps ?? 0; __codeWorkerProbe.updateWork += value.updateWorkMs ?? 0; if ((value.updateSteps ?? 0) > 0) __codeWorkerProbe.samples.push(value.updateWorkMs ?? 0); apply(value); }; })()"
    });
    const sustainedResult = await waitFor(async () => {
      const result = await connection.send("Runtime.evaluate", {
        expression: "window.__codeWorkerProbe",
        returnByValue: true
      });
      const value = result.result?.value;
      const requiredUpdates = capacityCounts.length > 0 ? 120 : (workloadName === "gravity-diagnostics" ? 65 : 30);
      return value?.updates >= requiredUpdates ? value : null;
    }, 20_000, `${browser.name} ${workloadName} sustained updates`);
    if (sustainedResult.draws < 1) throw new Error(`${browser.name} ${workloadName} completed no draws.`);
    const profileResult = await connection.send("Runtime.evaluate", {
      expression: "(async () => { const started = CodeRuntime.profile.start(); const isPromise = typeof started?.then === 'function'; await started; await new Promise(resolve => setTimeout(resolve, 150)); const report = await CodeRuntime.profile.report(); await CodeRuntime.profile.stop(); return { isPromise, instructionCount: report.instructionCount, opcodeCount: report.opcodes.length, hasFunctions: report.functions.length > 0, hasHostCalls: report.hostCalls.length > 0, runtime: report.runtime, backend: report.backend ?? report.runtime }; })()",
      awaitPromise: true,
      returnByValue: true
    });
    const profile = profileResult.result?.value;
    const profileShapeValid = webBackend === "direct-wasm"
      ? profile?.instructionCount === 0 && profile?.opcodeCount === 0 && profile?.backend === "direct-wasm"
      : profile?.instructionCount > 0 && profile?.opcodeCount > 0 && profile?.runtime === "rust-wasm";
    if (!profile?.isPromise || !profileShapeValid || !profile?.hasFunctions || !profile?.hasHostCalls) {
      throw new Error(`${browser.name} worker profiler API did not return asynchronous reports.`);
    }
    const beforeVisibility = sustainedResult.updates;
    await connection.send("Runtime.evaluate", {
      expression: "Object.defineProperty(document, 'hidden', { value: true, configurable: true }); document.dispatchEvent(new Event('visibilitychange'));"
    });
    await delay(200);
    await connection.send("Runtime.evaluate", {
      expression: "Object.defineProperty(document, 'hidden', { value: false, configurable: true }); document.dispatchEvent(new Event('visibilitychange'));"
    });
    await waitFor(async () => {
      const result = await connection.send("Runtime.evaluate", { expression: "window.__codeWorkerProbe", returnByValue: true });
      return result.result?.value?.updates >= beforeVisibility + 3;
    }, 10_000, `${browser.name} ${workloadName} visibility resume`);
    const sortedSamples = [...sustainedResult.samples].sort((left, right) => left - right);
    const median = sortedSamples[Math.ceil(sortedSamples.length * 0.5) - 1] ?? 0;
    const p95 = sortedSamples[Math.ceil(sortedSamples.length * 0.95) - 1] ?? 0;
    console.log(`[PASS] generated worker ${browser.name} ${workloadName} file:// smoke (${sustainedResult.updates} updates, ${sustainedResult.dropped} dropped, median ${median.toFixed(2)} ms, p95 ${p95.toFixed(2)} ms)`);
  } catch (error) {
    const suffix = diagnostics.length > 0 ? `\n${diagnostics.join("\n")}` : "";
    throw new Error(`${error.message}${suffix}`);
  } finally {
    connection?.close();
    child.kill();
    await Promise.race([new Promise(resolveExit => child.once("exit", resolveExit)), delay(2_000)]);
  }
}

try {
  const browsers = browserCandidates.filter((candidate, index, all) =>
    exists(candidate.path) && (!process.env.CODE_BROWSER || candidate.name.toLowerCase() === process.env.CODE_BROWSER.toLowerCase()) && all.findIndex(other => other.name === candidate.name && other.path === candidate.path) === index);
  if (browsers.length === 0) throw new Error("Chrome or Edge is required for the generated-worker smoke test.");
  for (const workload of workloads) {
    const outputDirectory = join(temporaryRoot, workload.name);
    if (installedCompiler) {
      execFileSync(installedCompiler, [workload.source, "-o", outputDirectory, "--web-backend", webBackend], { cwd: repositoryRoot, stdio: "ignore" });
    } else {
      execFileSync("dotnet", [
        "run", "--project", project, "-c", "Release", "--no-build", "--",
        workload.source, "-o", outputDirectory, "--web-backend", webBackend
      ], { cwd: repositoryRoot, stdio: "ignore" });
    }
    const pageUrl = pathToFileURL(join(outputDirectory, "index.html")).href;
    for (const browser of browsers) await runBrowserSmoke(browser, pageUrl, workload.name);
  }
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
