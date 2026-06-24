# Runtime Benchmarks

Run the deterministic JavaScript VM benchmarks from the repository root:

```powershell
node scripts/benchmark-runtime.mjs
```

Compare against a committed runtime from the same bytecode version without modifying the working tree:

```powershell
node scripts/benchmark-runtime.mjs --runtime-git-ref HEAD
```

The runtime ref must accept bytecode emitted by the current compiler. For the
v9-to-v10 transition, the sequential v9 baseline captured before implementation
was 129.1802 ms (`runtime_cpu`) and 121.6953 ms (`verlet_kernel`). The retained
v10 pass measured 100.6738 ms and 98.0664 ms respectively on the same machine
(22.1% and 19.4% lower median time). Treat these as transition records, not
portable performance claims.

The runner builds release bytecode, performs five warm-up runs, records twenty
samples, and reports median, p95, throughput, and coefficient of variation.
Working-tree runs fail when coefficient of variation exceeds `0.15`.
`runtime_cpu.code` covers loops, calls, arrays, fields, and arithmetic.
`verlet_kernel.code` performs unique-pair collision checks with eight substeps
and the standard `squareRoot` intrinsic. Keep workload constants and algorithms
unchanged when comparing runtime implementations.
`ball_regression.code` preserves the BallSimulator's intentionally duplicated
pair traversal and runs 130 objects without a broad phase. Run
`node scripts/benchmark-scheduler.mjs` to validate fixed-update counts across
common display refresh rates. The scheduler report includes update rate,
completed draws, update work, discarded steps, and 50 ms main-thread task
counts. Use `node scripts/test-generated-worker.mjs` for the real generated
worker path in installed Chrome and Edge.

Ball-style physics must integrate `Diagnostics.updateDeltaMilliseconds()`.
`lastFrameIntervalMilliseconds()` describes draw spacing and is not a physics
timestep, especially when worker updates and display refresh run independently.
`ball_regression_tests.code` separately covers zero, 100, and 130 objects,
deterministic repeated updates, and coincident centers without changing the
timed 130-object workload.
`ball_scene.code` is the generated-browser gate: it keeps the duplicated
O(n²) pair traversal, circular constraint, fixed 60 Hz scheduling, and 130
drawn objects while using `updateDeltaMilliseconds()` for integration.
`worker_diagnostics_gravity.code` is an end-to-end generated-worker regression
for authoritative frame diagnostics. It fails if updates never observe a
completed frame interval or if gravity cannot advance once timing is present.

For a local capacity sweep without modifying the checked-in O(n²) fixture:

```powershell
$env:CODE_BALL_COUNTS = "180,190,200,210,220"
$env:CODE_BROWSER = "Chrome"
node scripts/test-generated-worker.mjs
```

The alpha.9 pass sustained 200 balls for 120+ fixed updates with zero dropped
steps on the development machine (median 10.6 ms, p95 11.1 ms). At 210 balls
the run reported one dropped step, making 200 the conservative measured
capacity on that browser and machine.

The interactive Ball Simulator remains the end-to-end regression workload, but
its frame rate must not be compared with this controlled kernel unless object
counts, substeps, collision pairing, rendering, and broad-phase behavior match.

Run executable C# and JavaScript VM conformance checks with:

```powershell
node scripts/test-web-vm.mjs
```

Run the Rust/Wasm stop/go benchmark and executable parity suite with:

```powershell
node scripts/benchmark-rust-wasm.mjs
node scripts/test-rust-wasm.mjs
```

The benchmark runs JavaScript and Wasm sequentially in the same Chrome worker
with identical bytecode, five warm-ups, and twenty samples. Adoption requires a
1.5x geometric-mean median speedup across all three workloads, 1.5x on
`ball_regression`, no workload below 0.9x, and coefficient of variation at or
below 0.15. The retained full-parity pass measured 4.46x geometrically and
4.32x on `ball_regression` on the development machine; results are
machine/browser-specific and should be re-recorded for release candidates.

Run the typed direct-Wasm stop/go gate and executable parity suite with:

```powershell
node scripts/benchmark-direct-wasm.mjs
node scripts/test-direct-wasm.mjs
```

The direct benchmark compiles identical source to bytecode and native Wasm,
runs both sequentially in one Chrome worker, calibrates batches above browser
timer resolution, and reports compilation/startup separately. Adoption requires
2x geometric-mean throughput, 2x on `ball_regression`, no workload below 0.9x,
and CV at or below 0.15. The alpha.10 development pass measured 46.5x
geometrically and 60.5x on `ball_regression`; these machine-specific results do
not override conformance and memory-management gates.

The earlier C# Native-AOT experiment remains available after installing the
`.NET wasm-tools` workload. Run `node scripts/benchmark-csharp-wasm.mjs`; it reports AOT build
time, browser startup, raw and gzip-equivalent payload, managed memory, median,
p95, variance, and geometric-mean speedup against the JavaScript v10 VM. The
experiment implements only the two CPU benchmark opcode/host subsets; it is not
a shipping runtime or a parity claim.
