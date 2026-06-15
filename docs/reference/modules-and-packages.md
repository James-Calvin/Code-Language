# Modules and Packages

## Packages

A module may begin with a package declaration:

```code
package examples.math;
```

Rules:

- At most one package declaration per module.
- It must appear before imports and declarations.
- Matching package names enable `package` visibility.

Common mistakes:

- `package` after an import or declaration is rejected.
- Package visibility is not the same as a file path. The package names must match.

## Exports and Imports

Export a function:

```code
package examples.math;

public function<integer> add(integer left, integer right) {
  return left + right;
}
```

Import it:

```code
import add from "math.code";

print(add(2, 3));
```

Output:

```text
5
```

Import forms:

| Form | Example |
| --- | --- |
| Single import | `import add from "math.code";` |
| Alias import | `import add as plus from "math.code";` |
| Grouped import | `import { add, sub as minus } from "math.code";` |
| Namespace import | `import everything as Math from "math.code";` |
| Re-export single | `export import add from "math.code";` |
| Re-export grouped | `export import { add, sub } from "math.code";` |

Namespace imports are compile-time aliases for function-heavy module surfaces:

```code
import everything as Math from "math.code";

print(Math.add(2, 3));
```

Common mistakes:

- Namespace aliases cannot be used as runtime values.
- Namespace member access is for imported functions; do not expect it to expose arbitrary runtime objects.
- Imported symbols must be public or package-visible to the importer.

## Import Resolution

String-path imports search:

1. The importing file's directory.
2. The current project root `lib/` folder.
3. The installed compiler folder's bundled `lib/` folder.
4. Discovered ancestor `lib/` folders while walking upward from the importing file.

This is why engine wrappers work both from the repo root and from an installed release:

```code
import { rgb } from "engine/colors.code";
```

Common mistakes:

- Import paths are source file paths ending in `.code`.
- Manual release installs must keep the compiler executable beside the bundled `lib/` folder.
- Missing exports include import-chain diagnostics to help locate the failing dependency.

## Visibility

Top-level declarations:

```code
public function<integer> public_add(integer value) {
  return value + 1;
}

package function<integer> package_add(integer value) {
  return value + 2;
}

private function<integer> hidden_add(integer value) {
  return value + 3;
}
```

Behavior:

- `public` is importable from any module.
- `package` is importable only by modules with the same package name.
- `private` is module-local.
- Legacy `export` is accepted as an alias for public top-level declarations.
- Module-scope variables and constants are same-module globals only in V1. They are not imported or exported as public module API yet.

## Package Manifest

File name: `code.package.json`.

Minimal application:

```json
{
  "schemaVersion": 1,
  "name": "examples.app",
  "version": "0.1.0",
  "kind": "application",
  "entry": "main.code",
  "targets": ["vm-native", "vm-web"]
}
```

Fields:

| Field | Required | Notes |
| --- | --- | --- |
| `schemaVersion` | yes | Must be `1` |
| `name` | yes | Package name |
| `version` | yes | Semver |
| `kind` | yes | `application` or `library` |
| `entry` | yes | Existing `.code` path |
| `targets` | no | Allowed compile targets |
| `targetOverrides` | no | Parsed and validated; entry selection is not automatic yet |
| `hostAbi.requires` | no | Required target capabilities |
| `dependencies` | no | Local package dependencies |
| `devDependencies` | no | Parsed string map |

Host requirement example:

```json
{
  "schemaVersion": 1,
  "name": "examples.host.requirements.ok",
  "version": "0.1.0",
  "kind": "application",
  "entry": "main.code",
  "targets": ["vm-native", "vm-web"],
  "hostAbi": {
    "requires": ["std.time", "standard.input_output"]
  }
}
```

Common mistakes:

- `targets` rejects unsupported target builds at compile time.
- `hostAbi.requires` rejects capabilities unavailable on the selected target.
- `targetOverrides.entry` files must exist, but the compiler still follows the explicit entry path passed on the CLI.

## Lockfiles and Library Artifacts

When a manifest is present, compilation writes `code.lock.json` in the package root.

Lockfile contents:

- `schemaVersion`
- selected `target`
- resolved package list with `name`, `version`, `resolved`, and `integrity`

Library packages emit `.codelib` artifacts:

```json
{
  "schemaVersion": 1,
  "name": "examples.package.libraryartifact",
  "version": "0.1.0",
  "kind": "library",
  "entry": "main.code",
  "targets": ["vm-native", "vm-web"]
}
```

The artifact name follows:

```text
<package>-<version>-<target>.codelib
```

The CLI can run or disassemble `.codelib` files.

Common mistakes:

- Dependency versions currently support exact `x.y.z` and caret `^x.y.z` ranges.
- Dependency resolution is local, using `packages/`, segmented package paths, `lib/packages/`, and ancestor search.
