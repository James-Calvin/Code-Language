import { dotnet } from "./_framework/dotnet.js";

try {
  const runtime = await dotnet.create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  document.querySelector("#benchmark-result").textContent =
    exports.CodeWasmSpike.Benchmark.Run(performance.now() - window.codeWasmPageStarted);
} catch (error) {
  document.querySelector("#benchmark-result").textContent = JSON.stringify({ error: error?.stack ?? String(error) });
}
