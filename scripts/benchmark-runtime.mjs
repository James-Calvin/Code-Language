import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { performance } from "node:perf_hooks";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, "..");
const project = join(repositoryRoot, "ConsoleApp1", "ConsoleApp1.csproj");
const runtimePath = join(repositoryRoot, "web-runtime", "code-vm-web.js");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-runtime-benchmark-"));
const warmupRuns = 5;
const sampleRuns = 20;
const gitRefIndex = process.argv.indexOf("--runtime-git-ref");
const runtimeGitRef = gitRefIndex >= 0 ? process.argv[gitRefIndex + 1] : null;

function percentile(sorted, fraction) {
  return sorted[Math.min(sorted.length - 1, Math.ceil(sorted.length * fraction) - 1)];
}

function statistics(samples) {
  const sorted = [...samples].sort((a, b) => a - b);
  const average = samples.reduce((sum, value) => sum + value, 0) / samples.length;
  const variance = samples.reduce((sum, value) => sum + ((value - average) ** 2), 0) / samples.length;
  const medianMs = percentile(sorted, 0.5);
  return {
    medianMs,
    p95Ms: percentile(sorted, 0.95),
    runsPerSecond: 1000 / medianMs,
    coefficientOfVariation: Math.sqrt(variance) / average
  };
}

try {
  const runtimeSource = runtimeGitRef
    ? execFileSync("git", ["show", `${runtimeGitRef}:web-runtime/code-vm-web.js`], { cwd: repositoryRoot, encoding: "utf8" })
    : readFileSync(runtimePath, "utf8");
  const runtimeModule = await import(`data:text/javascript;base64,${Buffer.from(runtimeSource).toString("base64")}`);

  const workloads = ["runtime_cpu", "verlet_kernel", "ball_regression"];
  const results = [];
  for (const workload of workloads) {
    const sourcePath = join(repositoryRoot, "benchmarks", `${workload}.code`);
    const bytecodePath = join(temporaryRoot, `${workload}.bytecode`);
    execFileSync("dotnet", [
      "run", "--project", project, "-c", "Release", "--no-build", "--",
      "--target", "vm-web", "--compile-only", "-o", bytecodePath, sourcePath
    ], { cwd: repositoryRoot, stdio: "ignore" });

    const bytecode = new Uint8Array(readFileSync(bytecodePath));
    if (!runtimeGitRef) {
      const profileVm = new runtimeModule.WebVm(bytecode, { output: () => {}, profileEnabled: true });
      profileVm.run();
      const profile = profileVm.profiler.stop();
      if (profile.instructionCount <= 0 || profile.opcodes.length === 0 || profile.hostCalls.length === 0) {
        throw new Error(`Profiler smoke check failed for '${workload}'.`);
      }
      if (workload === "runtime_cpu" &&
          (profile.functions.length === 0 || profile.allocations.objects === 0 || profile.allocations.arrays === 0)) {
        throw new Error("Profiler did not account for runtime_cpu functions or allocations.");
      }
    }
    const execute = () => {
      const vm = new runtimeModule.WebVm(bytecode, { output: () => {} });
      vm.run();
    };

    try {
      for (let run = 0; run < warmupRuns; run += 1) execute();
    } catch (error) {
      results.push({
        runtime: runtimeGitRef ?? "working-tree",
        workload,
        skipped: true,
        reason: error instanceof Error ? error.message : String(error)
      });
      continue;
    }
    const samples = [];
    for (let run = 0; run < sampleRuns; run += 1) {
      const startedAt = performance.now();
      execute();
      samples.push(performance.now() - startedAt);
    }
    const result = { runtime: runtimeGitRef ?? "working-tree", workload, warmupRuns, sampleRuns, ...statistics(samples) };
    if (!runtimeGitRef && result.coefficientOfVariation > 0.15) {
      throw new Error(`Benchmark '${workload}' is unstable (coefficient of variation ${result.coefficientOfVariation}).`);
    }
    results.push(result);
  }

  console.table(results);
  console.log(JSON.stringify({ generatedAt: new Date().toISOString(), results }, null, 2));
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
