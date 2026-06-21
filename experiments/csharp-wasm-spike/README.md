# C# Native-AOT Wasm Spike

This bounded experiment executes the bytecode-v10 subsets used by
`runtime_cpu` and `verlet_kernel`. It deliberately uses tagged values,
contiguous operand/local/global/call stacks, handle-based arrays and objects,
numeric host binding IDs, and load-time instruction decoding instead of the
native reference VM's boxed `Stack<object>`, dictionary fields, and per-call
locals arrays.

Run from the repository root:

```powershell
node scripts/benchmark-csharp-wasm.mjs
```

The runner compiles current benchmark bytecode, publishes this project with
Native AOT, serves it to headless Chrome, and reports build time, startup,
payload, managed memory, throughput statistics, and relative speed against the
JavaScript v10 VM. This is not a parity runtime: unsupported opcodes and host
bindings fail immediately. Expansion is allowed only if the experiment clears
the documented 2x geometric-mean throughput gate.
