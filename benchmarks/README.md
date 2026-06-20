# Runtime Benchmarks

Run the deterministic JavaScript VM benchmarks from the repository root:

```powershell
node scripts/benchmark-runtime.mjs
```

Compare against a committed runtime without modifying the working tree:

```powershell
node scripts/benchmark-runtime.mjs --runtime-git-ref HEAD
```

The runner builds release bytecode, performs five warm-up runs, records twenty
samples, and reports median, p95, throughput, and coefficient of variation.
`runtime_cpu.code` covers loops, calls, arrays, fields, and arithmetic.
`verlet_kernel.code` performs unique-pair collision checks with eight substeps
and the standard `squareRoot` intrinsic. Keep workload constants and algorithms
unchanged when comparing runtime implementations.

The interactive Ball Simulator remains the end-to-end regression workload, but
its frame rate must not be compared with this controlled kernel unless object
counts, substeps, collision pairing, rendering, and broad-phase behavior match.

Run executable C# and JavaScript VM conformance checks with:

```powershell
node scripts/test-web-vm.mjs
```
