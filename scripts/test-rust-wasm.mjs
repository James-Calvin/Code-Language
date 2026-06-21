import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const project = join(root, "ConsoleApp1", "ConsoleApp1.csproj");
const runtimePath = join(root, "web-runtime", "code-vm-web.js");
const wasmPath = join(root, "web-runtime", "code-runtime.wasm");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-rust-wasm-conformance-"));
const cases = [
  join("ConsoleApp1", "examples", "arithmetic.code"),
  join("ConsoleApp1", "examples", "booleans.code"),
  join("ConsoleApp1", "examples", "strings.code"),
  join("ConsoleApp1", "examples", "loop.code"),
  join("ConsoleApp1", "examples", "branch.code"),
  join("ConsoleApp1", "examples", "function.code"),
  join("ConsoleApp1", "examples", "optional.code"),
  join("ConsoleApp1", "examples", "arrayloop.code"),
  join("ConsoleApp1", "examples", "modulo.code"),
  join("ConsoleApp1", "examples", "enum.code"),
  join("ConsoleApp1", "examples", "object.code"),
  join("ConsoleApp1", "examples", "record.code"),
  join("ConsoleApp1", "examples", "collections.code"),
  join("ConsoleApp1", "examples", "interface_dispatch.code"),
  join("ConsoleApp1", "examples", "interface_array_dispatch.code"),
  join("ConsoleApp1", "examples", "interface_field.code"),
  join("ConsoleApp1", "examples", "sized_numerics.code"),
  join("ConsoleApp1", "examples", "fallible.code"),
  join("benchmarks", "runtime_conformance.code"),
  join("benchmarks", "ball_regression.code"),
  join("benchmarks", "ball_regression_tests.code")
];

const runtimeSource = readFileSync(runtimePath, "utf8");
const runtime = await import(`data:text/javascript;base64,${Buffer.from(runtimeSource).toString("base64")}`);
let exports;
let bridge;
let output;

function readValue(pointer) {
  const view = new DataView(exports.memory.buffer);
  const payload = Number(view.getBigUint64(pointer, true));
  const tag = view.getUint32(pointer + 12, true);
  if (tag === 0) return view.getFloat64(pointer, true);
  if (tag === 3) {
    const stringPointer = exports.code_active_string_pointer(payload);
    const length = exports.code_active_string_length(payload);
    return new TextDecoder().decode(new Uint8Array(exports.memory.buffer, stringPointer, length));
  }
  if (tag === 1) {
    const length = exports.code_active_array_length(payload);
    return Array.from({ length }, (_, index) => exports.code_active_array_number(payload, index));
  }
  return { tag, payload };
}

function writeNumber(pointer, value) {
  const view = new DataView(exports.memory.buffer);
  view.setFloat64(pointer, Number(value), true);
  view.setUint32(pointer + 8, 0, true);
  view.setUint32(pointer + 12, 0, true);
}

const wasm = await WebAssembly.instantiate(readFileSync(wasmPath), { code_host: {
  call: (context, bindingId, argumentsPointer, argumentCount, resultPointer) => {
    try {
      const metadata = bridge.metadata.hostBindings[bindingId];
      const binding = bridge.hostBindings.get(metadata.symbol);
      if (!binding || binding.arity !== argumentCount) return 1;
      const argumentsList = Array.from({ length: argumentCount }, (_, index) => readValue(argumentsPointer + index * 16));
      const result = binding.handler(argumentsList);
      if (typeof result !== "number") return 1;
      writeNumber(resultPointer, result);
      return 0;
    } catch { return 1; }
  },
  output: (context, valuePointer) => output.push(String(readValue(valuePointer))),
  unix_milliseconds: () => Date.now(),
  monotonic_milliseconds: () => performance.now()
} });
exports = wasm.instance.exports;

try {
  for (let index = 0; index < cases.length; index += 1) {
    const relativeSource = cases[index];
    const sourcePath = join(root, relativeSource);
    const bytecodePath = join(temporaryRoot, `case-${index}.bytecode`);
    execFileSync("dotnet", ["run", "--project", project, "-c", "Release", "--no-build", "--", "--target", "vm-web", "--compile-only", "-o", bytecodePath, sourcePath], { cwd: root, stdio: "ignore" });
    const expected = execFileSync("dotnet", ["run", "--project", project, "-c", "Release", "--no-build", "--", "--target", "vm-web", bytecodePath], { cwd: root, encoding: "utf8" }).replaceAll("\r\n", "\n");
    const bytecode = new Uint8Array(readFileSync(bytecodePath));
    output = [];
    bridge = new runtime.WebVm(bytecode, { output: () => {} });
    const pointer = exports.code_alloc(bytecode.byteLength);
    new Uint8Array(exports.memory.buffer, pointer, bytecode.byteLength).set(bytecode);
    const status = exports.code_run(pointer, bytecode.byteLength);
    exports.code_dealloc(pointer, bytecode.byteLength);
    if (status !== 0) throw new Error(`${relativeSource}: Rust/Wasm VM status ${status}.`);
    const actual = output.length === 0 ? "" : `${output.join("\n")}\n`;
    if (actual !== expected) throw new Error(`${relativeSource}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}.`);
    console.log(`[PASS] rust-wasm ${relativeSource}`);
  }
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
