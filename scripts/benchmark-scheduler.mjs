import { readFileSync } from "node:fs";
import { performance } from "node:perf_hooks";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const source = readFileSync(join(root, "web-runtime", "code-vm-web.js"), "utf8");
const runtimeModule = await import(`data:text/javascript;base64,${Buffer.from(source).toString("base64")}`);
globalThis.requestAnimationFrame = () => 1;
globalThis.cancelAnimationFrame = () => {};

const results = [];
const scenarios = [
  { updateRate: 60, displayRate: 30 },
  ...[60, 120, 240].flatMap(updateRate => [60, 120, 144].map(displayRate => ({ updateRate, displayRate })))
];
for (const { updateRate, displayRate } of scenarios) {
  const runtime = new runtimeModule.CanvasSceneRuntime();
  let updates = 0;
  let draws = 0;
  runtime.running = true;
  runtime.sceneObject = {};
  runtime.sceneInfo = { update: { targetIp: 1, frameSize: 1 }, draw: { targetIp: 2, frameSize: 1 }, drawHud: null };
  runtime.vm = { invokeVoid: ip => { if (ip === 1) updates += 1; else draws += 1; } };
  runtime.lastTimestampMs = 0;
  runtime.lastDrawTimestampMs = 0;
  runtime.setFixedUpdateRate(updateRate);
  const started = performance.now();
  const frameCount = displayRate * 10;
  let updateWorkMs = 0;
  let droppedSteps = 0;
  let longMainThreadTasks = 0;
  for (let frame = 1; frame <= frameCount; frame += 1) {
    const taskStarted = performance.now();
    runtime.onFrame(frame * 1000 / displayRate);
    const taskWork = performance.now() - taskStarted;
    updateWorkMs += runtime.lastFrameUpdateWorkMs;
    droppedSteps += runtime.lastDroppedUpdateSteps();
    if (taskWork >= 50) longMainThreadTasks += 1;
  }
  results.push({
    configuredUpdateRate: updateRate, displayRate, completedUpdates: updates,
    measuredUpdateRate: updates / 10, completedDraws: draws,
    updateWorkMs, droppedSteps, longMainThreadTasks,
    benchmarkElapsedMs: performance.now() - started
  });
}

console.table(results);
for (const result of results) {
  if (Math.abs(result.completedUpdates - result.configuredUpdateRate * 10) > 1 || result.completedDraws !== result.displayRate * 10 || result.droppedSteps !== 0)
    throw new Error(`Scheduler stability failed at ${result.displayRate} Hz: ${JSON.stringify(result)}`);
}
