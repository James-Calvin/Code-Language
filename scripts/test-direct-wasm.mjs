import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const compiler = join(root, "ConsoleApp1", "bin", "Release", "net9.0", "compiler.dll");
const temporaryRoot = mkdtempSync(join(tmpdir(), "code-direct-wasm-conformance-"));
const cases = [
  "arithmetic.code", "booleans.code", "strings.code", "loop.code", "branch.code", "function.code",
  "optional.code", "arrayloop.code", "modulo.code", "enum.code", "object.code", "record.code",
  "collections.code", "interface_dispatch.code", "interface_array_dispatch.code", "interface_field.code",
  "sized_numerics.code", "fallible.code",
  "benchmarks/runtime_conformance.code", "benchmarks/ball_regression.code", "benchmarks/ball_regression_tests.code"
];

function instantiate(bytes, output) {
  let instance;
  const strings = [""];
  const stringIds = new Map([["", 0]]);
  const addString = value => {
    const text = String(value);
    if (stringIds.has(text)) return stringIds.get(text);
    strings.push(text); const handle = strings.length - 1; stringIds.set(text, handle); return handle;
  };
  const collections = [null];
  const runtime = new Proxy({
    string_from_utf8: (pointer, length) => addString(new TextDecoder().decode(new Uint8Array(instance.exports.memory.buffer, pointer, length))),
    string_concat: (left, right) => addString((strings[left] ?? "") + (strings[right] ?? "")),
    string_equal: (left, right) => (strings[left] ?? "") === (strings[right] ?? "") ? 1 : 0,
    string_from_i32: value => addString(value ? "1" : "0"),
    string_from_i64: value => addString(String(value)),
    string_from_f64: value => addString(String(value)),
    collection_new: kind => { collections.push({ kind, items: [], map: new Map(), set: new Set() }); return collections.length - 1; },
    collection_length: handle => BigInt(collections[handle].kind === 1 ? collections[handle].map.size : collections[handle].kind === 2 ? collections[handle].set.size : collections[handle].items.length)
  }, { get(target, name) {
    if (name in target) return target[name];
    const operation = String(name);
    if (operation.startsWith("map_set_")) return (handle, key, value) => collections[handle].map.set(key, value);
    if (operation.startsWith("map_get_")) return (handle, key) => collections[handle].map.get(key);
    if (operation.startsWith("collection_add_")) return (handle, value) => { const item = collections[handle]; if (item.kind === 2) item.set.add(value); else item.items.push(value); };
    if (operation.startsWith("collection_contains_")) return (handle, value) => { const item = collections[handle]; return (item.kind === 1 ? item.map.has(value) : item.kind === 2 ? item.set.has(value) : item.items.includes(value)) ? 1 : 0; };
    if (operation.startsWith("collection_remove_")) return (handle, value) => { const item = collections[handle]; if (item.kind === 1) item.map.delete(value); else if (item.kind === 2) item.set.delete(value); else { const index = item.items.indexOf(value); if (index >= 0) item.items.splice(index, 1); } };
    if (operation.startsWith("collection_peek_")) return handle => { const item = collections[handle]; return item.kind === 4 ? item.items[item.items.length - 1] : item.items[0]; };
    if (operation.startsWith("collection_pop_")) return handle => { const item = collections[handle]; return item.kind === 4 ? item.items.pop() : item.items.shift(); };
    return undefined;
  } });
  const host = new Proxy({
    print_i32: value => output.push(String(value)),
    print_i64: value => output.push(String(value)),
    print_f64: value => output.push(String(value)),
    print_string: handle => output.push(strings[handle] ?? ""),
    panic_string: handle => { throw new Error(strings[handle] ?? "Direct-Wasm panic"); }
  }, {
    get(target, symbol) {
      if (symbol in target) return target[symbol];
      return (...args) => {
        switch (String(symbol)) {
          case "std.math.sine": return Math.sin(args[0]);
          case "std.math.cosine": return Math.cos(args[0]);
          case "std.math.random": return Math.random();
          case "std.time.unix_ms": return BigInt(Date.now());
          case "std.time.unix_us": return BigInt(Date.now() * 1000);
          case "std.time.mono_ns": return BigInt(Math.trunc(performance.now() * 1_000_000));
          case "std.time.mono_ticks": return BigInt(Math.trunc(performance.now() * 1000));
          case "std.time.mono_ticks_per_second": return 1_000_000n;
          case "engine.diagnostics.last_update_steps_scene":
          case "engine.diagnostics.last_dropped_update_steps_scene": return 0n;
          default: return 0;
        }
      };
    }
  });
  instance = new WebAssembly.Instance(new WebAssembly.Module(bytes), { code_host: host, code_runtime: runtime });
  return instance;
}

try {
  const failures = [];
  for (let index = 0; index < cases.length; index += 1) {
    const name = cases[index];
    const source = name.includes("/") ? join(root, ...name.split("/")) : join(root, "ConsoleApp1", "examples", name);
    const bytecode = join(temporaryRoot, `${index}.bytecode`);
    const appWasm = join(temporaryRoot, `${index}.wasm`);
    try {
      execFileSync("dotnet", [compiler, "--target", "vm-web", "--compile-only", "-o", bytecode, source], { cwd: root, stdio: "ignore" });
      const expected = execFileSync("dotnet", [compiler, "--target", "vm-web", bytecode], { cwd: root, encoding: "utf8" }).replaceAll("\r\n", "\n");
      execFileSync("dotnet", [compiler, "--target", "vm-web", "--compile-only", "--web-backend", "direct-wasm", "-o", appWasm, source], { cwd: root, stdio: "pipe" });
      const output = [];
      const instance = instantiate(readFileSync(appWasm), output);
      const status = instance.exports.code_run();
      if (status !== 0) throw new Error(`status ${status}`);
      const actual = output.length === 0 ? "" : `${output.join("\n")}\n`;
      if (actual !== expected) throw new Error(`expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
      console.log(`[PASS] direct-wasm ${name}`);
    } catch (error) {
      const detail = error.stderr?.toString().trim() || error.message;
      failures.push(`${name}: ${detail}`);
      console.error(`[FAIL] direct-wasm ${name}: ${detail}`);
    }
  }
  if (failures.length > 0) throw new Error(`${failures.length} direct-Wasm conformance case(s) failed.`);
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
