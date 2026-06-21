import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "..");
const project = join(repositoryRoot, "ConsoleApp1", "ConsoleApp1.csproj");
const runtimePath = join(repositoryRoot, "web-runtime", "code-vm-web.js");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-web-vm-conformance-"));

const cases = [
  join("ConsoleApp1", "examples", "arithmetic.code"),
  join("ConsoleApp1", "examples", "object.code"),
  join("ConsoleApp1", "examples", "record.code"),
  join("ConsoleApp1", "examples", "collections.code"),
  join("ConsoleApp1", "examples", "interface_dispatch.code"),
  join("ConsoleApp1", "examples", "sized_numerics.code"),
  join("ConsoleApp1", "examples", "fallible.code"),
  join("benchmarks", "runtime_conformance.code"),
  join("benchmarks", "ball_regression.code"),
  join("benchmarks", "ball_regression_tests.code")
];

try {
  const runtimeSource = readFileSync(runtimePath, "utf8");
  const runtimeModule = await import(`data:text/javascript;base64,${Buffer.from(runtimeSource).toString("base64")}`);

  for (let index = 0; index < cases.length; index += 1) {
    const relativeSource = cases[index];
    const sourcePath = join(repositoryRoot, relativeSource);
    const bytecodePath = join(temporaryRoot, `case-${index}.bytecode`);
    execFileSync("dotnet", [
      "run", "--project", project, "-c", "Release", "--no-build", "--",
      "--target", "vm-web", "--compile-only", "-o", bytecodePath, sourcePath
    ], { cwd: repositoryRoot, stdio: "ignore" });

    const expected = execFileSync("dotnet", [
      "run", "--project", project, "-c", "Release", "--no-build", "--",
      "--target", "vm-web", bytecodePath
    ], { cwd: repositoryRoot, encoding: "utf8" }).replaceAll("\r\n", "\n");

    const output = [];
    const vm = new runtimeModule.WebVm(new Uint8Array(readFileSync(bytecodePath)), {
      output: line => output.push(String(line))
    });
    vm.run();
    const actual = output.length === 0 ? "" : `${output.join("\n")}\n`;
    if (actual !== expected) {
      throw new Error(`${relativeSource}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
    }
    if (relativeSource.endsWith("ball_regression_tests.code") && actual !== "1\n1\n1\n1\n") {
      throw new Error(`${relativeSource}: Ball regression assertions failed: ${JSON.stringify(actual)}`);
    }
    console.log(`[PASS] web-vm ${relativeSource}`);
  }

  globalThis.requestAnimationFrame = () => 1;
  globalThis.cancelAnimationFrame = () => {};
  const sceneRuntime = new runtimeModule.CanvasSceneRuntime();
  let updateCalls = 0;
  sceneRuntime.running = true;
  sceneRuntime.lastTimestampMs = 0;
  sceneRuntime.sceneObject = {};
  sceneRuntime.sceneInfo = {
    update: { targetIp: 1, frameSize: 1 },
    draw: { targetIp: 2, frameSize: 1 },
    drawHud: null
  };
  sceneRuntime.vm = {
    invokeVoid: targetIp => { if (targetIp === 1) updateCalls += 1; }
  };
  sceneRuntime.scheduleUpdatePump = () => {};
  sceneRuntime.setFixedUpdateRate(60);
  sceneRuntime.onFrame(250);
  if (updateCalls !== 5 || sceneRuntime.lastUpdateSteps() !== 5 || sceneRuntime.lastDroppedUpdateSteps() < 9) {
    throw new Error("Fixed-step catch-up cap or dropped-step diagnostics failed.");
  }
  console.log("[PASS] web-runtime fixed-step catch-up cap");

  const completedUpdateWork = sceneRuntime.lastUpdateWorkMilliseconds();
  sceneRuntime.onFrame(251);
  if (sceneRuntime.lastUpdateSteps() !== 0 || sceneRuntime.lastUpdateWorkMilliseconds() !== completedUpdateWork) {
    throw new Error("A zero-update draw erased the previous completed-update diagnostic.");
  }
  console.log("[PASS] web-runtime stable update diagnostics");

  sceneRuntime.lastUpdateWorkMs = 75;
  let recoverableObjectCount = 130;
  sceneRuntime.onFrame(252);
  if (sceneRuntime.lastUpdateWorkMilliseconds() > 50) recoverableObjectCount -= 10;
  if (recoverableObjectCount !== 120) {
    throw new Error("A zero-update draw prevented overload recovery from observing the previous update.");
  }
  console.log("[PASS] web-runtime overload recovery diagnostic");

  if (Math.abs(sceneRuntime.updateDeltaMilliseconds() - (1000 / 60)) > 0.001) {
    throw new Error("Fixed scheduler did not publish its exact simulation delta.");
  }
  console.log("[PASS] web-runtime fixed update delta");

  sceneRuntime.running = true;
  sceneRuntime.useContinuousUpdates();
  updateCalls = 0;
  sceneRuntime.onUpdatePump();
  if (updateCalls < 1) throw new Error("Continuous scheduler did not execute updates.");
  console.log("[PASS] web-runtime continuous scheduler");

  sceneRuntime.setFixedUpdateRate(120);
  if (sceneRuntime.updateMode !== "fixed" || Math.abs(sceneRuntime.stepMs - (1000 / 120)) > 0.001) {
    throw new Error("Runtime fixed-rate mode change failed.");
  }
  sceneRuntime.setMaximumRenderRate(30);
  if (sceneRuntime.maximumRenderRate !== 30) throw new Error("Runtime render cap failed.");
  sceneRuntime.useDisplaySynchronizedRendering();
  if (sceneRuntime.maximumRenderRate !== 0) throw new Error("Display-synchronized rendering restore failed.");
  for (const invalid of [0, -1, 1.5, Number.NaN]) {
    let rejected = false;
    try { sceneRuntime.setFixedUpdateRate(invalid); } catch { rejected = true; }
    if (!rejected) throw new Error(`Invalid fixed update rate ${invalid} was accepted.`);
  }
  console.log("[PASS] web-runtime scheduling configuration");

  const cappedRuntime = new runtimeModule.CanvasSceneRuntime();
  let cappedUpdates = 0;
  let cappedDraws = 0;
  cappedRuntime.running = true;
  cappedRuntime.sceneObject = {};
  cappedRuntime.sceneInfo = { update: { targetIp: 1, frameSize: 1 }, draw: { targetIp: 2, frameSize: 1 }, drawHud: null };
  cappedRuntime.vm = { invokeVoid: ip => { if (ip === 1) cappedUpdates += 1; else cappedDraws += 1; } };
  cappedRuntime.lastTimestampMs = 0;
  cappedRuntime.lastDrawTimestampMs = 0;
  cappedRuntime.setFixedUpdateRate(120);
  cappedRuntime.setMaximumRenderRate(30);
  for (const timestamp of [10, 20, 30, 40]) cappedRuntime.onFrame(timestamp);
  if (cappedUpdates <= cappedDraws || cappedDraws !== 1) {
    throw new Error("Render cap incorrectly stopped fixed updates.");
  }
  console.log("[PASS] web-runtime independent render cap");

  sceneRuntime.lastUpdateTimestampMs = performance.now() - 3;
  sceneRuntime.executeUpdateStep(7);
  if (sceneRuntime.lastUpdateIntervalMilliseconds() <= 0) throw new Error("Update interval diagnostic was not measured.");
  if (sceneRuntime.updateDeltaMilliseconds() !== 7) throw new Error("Update delta diagnostic was not measured.");
  console.log("[PASS] web-runtime update interval diagnostic");

  const workerHost = new runtimeModule.WorkerSceneRuntime(() => {});
  workerHost.setDrawSpace("world");
  workerHost.clear(1, 2, 3, 1);
  workerHost.drawCircle(10, 20, 5, 4, 5, 6, 1);
  workerHost.drawText("worker", 3, 4, 12, "left", "top", 7, 8, 9, 1);
  const replayed = [];
  const replayHost = {
    setDrawSpace: value => replayed.push(["space", value]),
    clear: (...values) => replayed.push(["clear", ...values]),
    drawCircle: (...values) => replayed.push(["circle", ...values]),
    drawText: (...values) => replayed.push(["text", ...values])
  };
  runtimeModule.replayDrawCommands(replayHost, new Float64Array(workerHost.commandNumbers), workerHost.commandStrings);
  if (replayed.length !== 4 || replayed[2][0] !== "circle" || replayed[3][1] !== "worker") {
    throw new Error("Worker draw command recording/replay failed.");
  }
  console.log("[PASS] web-runtime worker draw command buffer");

  workerHost.applyViewport({ safeWidth: 800, safeHeight: 450, worldScale: 2 });
  workerHost.applyInput({
    keysDown: [65], pointerScreenX: 12, pointerScreenY: 34, pointerIsDownNow: true,
    pointerPressed: true, pointerReleased: false, audioUnlocked: true
  });
  workerHost.applyInput({
    keysDown: [65], pointerScreenX: 12, pointerScreenY: 34, pointerIsDownNow: true,
    pointerPressed: false, pointerReleased: false, audioUnlocked: true
  });
  workerHost.beginFixedUpdateStep();
  workerHost.applyAudioStatus(9, true);
  if (workerHost.safeWidth !== 800 || workerHost.worldScale !== 2 || !workerHost.keysDown.has(65)
      || !workerHost.pointerWasPressedForStep || !workerHost.soundIsPlaying(9)) {
    throw new Error("Worker viewport, input-edge, or audio-status delivery failed.");
  }
  workerHost.applyAudioStatus(9, false);
  if (workerHost.soundIsPlaying(9)) throw new Error("Worker audio stop status was not applied.");
  console.log("[PASS] web-runtime worker protocol state delivery");

  let workerUpdateCalls = 0;
  workerHost.vm = { invokeVoid: () => { workerUpdateCalls += 1; } };
  workerHost.sceneInfo = { update: { targetIp: 1, frameSize: 1 } };
  workerHost.sceneObject = {};
  workerHost.running = true;
  workerHost.updateMode = "fixed";
  workerHost.stepMs = 1000 / 60;
  workerHost.accumulatorMs = 0;
  workerHost.lastUpdatePumpTimestampMs = performance.now() - 250;
  workerHost.scheduleWorkerPump = () => {};
  for (let turn = 0; turn < 5; turn += 1) workerHost.workerPump();
  if (workerUpdateCalls !== 5 || workerHost.pendingDroppedUpdateSteps < 1) {
    throw new Error("Worker scheduler did not enforce one update per turn and its five-turn catch-up cap.");
  }
  console.log("[PASS] web-runtime worker fixed-step scheduling");

  const updatesBeforeHiddenPump = workerUpdateCalls;
  workerHost.setWorkerHidden(true);
  workerHost.workerPump();
  if (workerUpdateCalls !== updatesBeforeHiddenPump) throw new Error("Hidden worker executed an update.");
  workerHost.scheduleWorkerPump = () => {};
  workerHost.lastUpdateTimestampMs = 42;
  workerHost.accumulatorMs = 100;
  workerHost.setWorkerHidden(false);
  if (workerHost.lastUpdateTimestampMs !== 0 || workerHost.accumulatorMs !== 0) {
    throw new Error("Worker resume did not reset scheduler timing.");
  }
  console.log("[PASS] web-runtime worker visibility scheduling");

  let completedWorkerFrame = null;
  const diagnosticWorker = new runtimeModule.WorkerSceneRuntime(message => {
    if (message.type === "frame") completedWorkerFrame = message;
  });
  diagnosticWorker.vm = { invokeVoid: () => {} };
  diagnosticWorker.sceneObject = {};
  diagnosticWorker.sceneInfo = { draw: { targetIp: 2, frameSize: 1 }, drawHud: null };
  diagnosticWorker.lastDrawTimestampMs = 100;
  diagnosticWorker.pendingUpdateWorkMs = 4;
  diagnosticWorker.pendingUpdateSteps = 2;
  diagnosticWorker.pendingDroppedUpdateSteps = 1;
  diagnosticWorker.renderWorkerFrame(1, 116, null, null);
  if (diagnosticWorker.lastFrameIntervalMilliseconds() !== 16
      || diagnosticWorker.lastUpdateSteps() !== 2
      || diagnosticWorker.lastDroppedUpdateSteps() !== 1
      || completedWorkerFrame?.diagnostics?.frameIntervalMs !== 16) {
    throw new Error("Completed worker-frame diagnostics were not published into authoritative VM state.");
  }

  let gravityPosition = 0;
  let previousGravityPosition = 0;
  diagnosticWorker.sceneInfo.update = { targetIp: 1, frameSize: 1 };
  diagnosticWorker.vm = {
    invokeVoid: targetIp => {
      if (targetIp !== 1) return;
      const deltaSeconds = diagnosticWorker.lastFrameIntervalMilliseconds() / 1000;
      const velocity = gravityPosition - previousGravityPosition;
      previousGravityPosition = gravityPosition;
      gravityPosition += velocity + 1600 * deltaSeconds * deltaSeconds;
    }
  };
  diagnosticWorker.executeUpdateStep(1000 / 60);
  if (!(gravityPosition > 0)) {
    throw new Error("Worker frame-interval diagnostics suppressed gravity integration.");
  }
  console.log("[PASS] web-runtime worker authoritative diagnostics and gravity");
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
