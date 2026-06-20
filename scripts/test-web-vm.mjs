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
  join("benchmarks", "runtime_conformance.code")
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
    console.log(`[PASS] web-vm ${relativeSource}`);
  }

  globalThis.requestAnimationFrame = () => 0;
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
  sceneRuntime.onFrame(250);
  if (updateCalls !== 5 || sceneRuntime.lastUpdateSteps() !== 5 || sceneRuntime.lastDroppedUpdateSteps() < 9) {
    throw new Error("Fixed-step catch-up cap or dropped-step diagnostics failed.");
  }
  console.log("[PASS] web-runtime fixed-step catch-up cap");
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
