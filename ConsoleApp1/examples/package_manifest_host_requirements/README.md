# Package Manifest Host Requirement Examples

This folder has two package examples for target/capability behavior.

## `ok/`
- Declares `hostAbi.requires: ["std.time", "standard.input_output"]`.
- Works on `vm-native` and `vm-web`.

Compile:
```
dotnet run --project ConsoleApp1 -- --target vm-web --compile-only ConsoleApp1/examples/package_manifest_host_requirements/ok/main.code
```

## `web_blocked/`
- Declares `hostAbi.requires: ["std.fs"]`.
- Compiles on `vm-native`.
- Fails on `vm-web` with a capability error.

Compile (expected failure):
```
dotnet run --project ConsoleApp1 -- --target vm-web --compile-only ConsoleApp1/examples/package_manifest_host_requirements/web_blocked/main.code
```

