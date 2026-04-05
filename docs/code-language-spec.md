# Code Language Specification (Living Draft)

Version: 1.0
Last updated: 2026-04-05

## 1. Goals and Design
- `Code` is a code-first language for building 2D interactive applications that target the web first.
- It prioritizes clarity over brevity.
- Syntax should be consistent and intuitive.
- It targets new developers while teaching concepts transferable to other languages.
- Its primary product direction is: write Code source, build it, and get a runnable website.
- It should feel closer to game development than to DOM-first web development.
- It supports object-oriented programming.
- It does not use class inheritance.
- It uses interfaces for structural contracts.
- It should preserve native portability as a secondary goal, but the first-class workflow is browser deployment.
- When ambiguous, behavior should follow new-developer intuition.

## 2. Program Structure
- Smallest valid program (current):

```code
print("hello, world");
```

- Console application entry point:
  - `main` is optional.
  - `main` is used for terminal-executed console apps that take command-line arguments.
  - A file can execute without `main` when no command-line parameters are required.
  - This is the current CLI/runtime behavior; the planned web-app authoring contract is documented separately in `docs/web-app-v1.md`.
  - Signature:

```code
function main(string[] arguments) {
  // first argument is the name of the executable
  if arguments.length < 2 then {
    print("usage: {arguments[0]} <your name>");
    return;
  } else {
    print("hello {arguments[1]}");
  }
}
```

## 3. Lexical Rules, Statements, and Delimiters
- Identifiers must start with a letter or `_`.
- After the first character, identifiers may contain letters, digits, and `_`.
- Keywords are inferred from the syntax definition; an explicit full keyword table is deferred.
- Semicolons are required.
- Semicolon injection will exist in the future, but exact insertion rules are deferred.

## 4. Type System (Current Decisions)
- Type annotations are required.
- No implicit omission of variable types.
- Local variables may be declared without initializer, but must be definitely assigned before first read.
- Primitive numeric families:
  - `integer`: signed integer
  - `whole`: unsigned integer
  - `real`: IEEE-754 floating point
- Boolean type: `boolean`
- String type: `string`
- Sized numeric variants:
  - `integer`: `integer8`, `integer16`, `integer32`, `integer64`
  - `whole`: `whole8`, `whole16`, `whole32`, `whole64`
  - `real`: `real16`, `real32`, `real64`
- Unsized `integer`, `whole`, and `real` default bit width is runtime-chosen (typically 64-bit).
- Numeric literals:
  - Decimal by default; `_` digit separators allowed between digits.
  - Base prefixes: `0b` (binary), `0o` (octal), `0x` (hex).
  - Sized suffixes: `i8/i16/i32/i64` for signed, `w8/w16/w32/w64` for unsigned, `r16/r32/r64` for reals.
  - Unsuffixed literals map to unsized `integer/whole/real`; no implicit narrowing from larger/suffixed forms.
- Conversions and promotions:
  - Explicit casts use `as Type`.
  - Implicit promotions only for lossless widening within a numeric family and from `integer` to `real`.
  - No implicit sign changes (`whole` to `integer` requires `as integer`); no implicit downcasts.

Examples:

```code
integer value = 0;
value += 1;
whole count = 0;
constant integer maxRetries = 3;
whole8 red = 255;
boolean flag = false;
```

Definite assignment examples:

```code
integer x;
x = 1;
print(x);
```

```code
integer y = 1;
print(y);
```

## 5. Nullability and Optionals
- `null` is not part of the language model.
- Absence is represented with `optional<T>`.
- Optionals expose `hasValue` for presence checks.
- Flow narrowing: inside `if maybe.hasValue then { ... }`, `maybe` is treated as the contained `T`; outside it remains `optional<T>`.
- Accessors:
  - `maybe.value` unwraps and panics if empty.
  - `maybe.or(fallbackExpression)` returns the value or the fallback without panicking.

```code
optional<integer> maybeCount = getCount();
if maybeCount.hasValue then {
  // use value
}
```

## 6. Functions
- Declaration syntax:
  - With explicit return type: `function<ReturnType> name(parameters) { ... }`
  - `void` return may be implied by omitting `<void>`.
  - The same implicit-void form applies to methods declared inside `object` bodies.

```code
function<void> doWork(integer parameter) { ... }
function doWork(integer parameter) { ... }
```

- Return and call examples:

```code
function<boolean> isOdd(integer value) {
  return (value & 1) == 1;
}
print(isOdd(15));
```

```code
function<integer> add(integer parameter1, integer parameter2) {
  return parameter1 + parameter2;
}
```
- Overload resolution:
  - Prefer exact parameter type matches.
  - Otherwise choose the candidate requiring the fewest implicit promotions; if tied, choose the one with the lowest-rank promotions.
  - Non-variadic matches beat variadics when both apply.
  - Remaining ambiguity results in a compile error (declaration order is not a tie-breaker).

## 7. Control Flow, Loops, and Operators
- `if` uses `then`.
- `then` is mandatory in all `if` forms.
- `if` supports block and single-statement forms.
- `else` is supported with block form.

```code
if condition then {
  // branch
} else {
  // branch
}
```

```code
if myData_ < 0 then return;
if myData_ < 0 then doSomething();
```

- `while` supports single-statement and block forms.
- `then` is mandatory after `while` conditions (and after `for`/`foreach` headers).

```code
while count > 0 then count -= 1;
while count > 0 then {
  // do stuff
}
```

- Counted `for` loop syntax:

```code
for integer index = 0; index < 100; index++ then {
  // stuff
}
```

- `foreach` loop syntax:

```code
foreach number in numbers then print(number);
foreach number in numbers then {
  // stuff
}
```

- `break` and `continue` are supported in loops.

```code
while value != someValue then {
  if value == someOtherValue then break;
  if value == anotherValue then continue;
  // ...
}
```

- Arrays (current impl): array literal syntax `{a, b, c}` builds a runtime array; typed declarations `array<integer> xs = {1,2,3};`; dynamic `new array<integer>(n)` requires a size; `xs.length` yields length; `xs[index]` reads an element; `xs[index] = value` writes an element; `foreach` can iterate arrays by element in addition to numeric bounds.
- Optionals (current impl): `optional<T>` types store `none` or a value; `none` literal; `opt.hasValue` returns boolean; `opt.value` returns contained value or panics if empty; `opt.or(fallback)` returns value or fallback without panicking.

```code
foreach number in numbers then {
  if number < 0 then break;
  if isPrime(number) then continue;
}
```

- Logical operators:
  - Conjunction uses `and`.
  - Disjunction uses `or`.
  - Negation uses unary `not`.
  - `or` here is the boolean operator; it is distinct from the `optional.or(...)` helper method.

```code
if a > 0 and b > 0 then {
  // branch
}

if a > 0 or b > 0 then {
  // branch
}

if not isReady then {
  panic("Not ready");
}
```

- Operator precedence and associativity (aligned with C#) from highest to lowest:
  1. Unary prefix: `+x`, `-x`, `not x`
  2. Multiplicative: `*`, `/`, `%`
  3. Additive: `+`, `-`
  4. Relational: `<`, `<=`, `>`, `>=`
  5. Equality: `==`, `!=`
  6. Logical conjunction: `and`
  7. Logical disjunction: `or`
  8. Assignment: `=`
  - Binary operators are left-associative except assignment, which is right-associative.
  - Parentheses may be used to override precedence.
- Arithmetic includes modulo (`%`).
- Enhanced assignments are supported for variables:
  - `+=`, `-=`, `*=`, `/=`, `%=`
  - postfix `++`, `--`

## 8. Collections (Observed)
- Generic array type syntax: `array<Type>`.
- Array literal syntax uses braces.
- Arrays can be allocated with `new array<Type>(size)`.

```code
array<integer> numbers = {1, 1, 2, 3, 5, 8, 13};
array<integer> otherNumbers = new array<integer>(10);
```

## 9. Object Model and Interfaces
- Current implementation status:
  - Implemented: object declarations with fields/constructors/methods, `new Type(...)`, object field read/write (`obj.field`, `obj.field = value`), method calls (`obj.method(args)`), explicit interface conformance checks via `implement Interface for Object`, and interface-typed locals/parameters/returns/fields with runtime-dispatched interface method calls.
  - Not yet implemented: interface-typed collection/container surfaces, visibility enforcement, records.
- No inheritance.
- Contracts are declared as `interface`.
- Concrete types are declared as `object`.
- Objects can implement multiple interfaces.
- `implement Interface for Object` is required for interface fulfillment.
- Interface methods must declare explicit return and parameter types.
- Methods may also be declared directly inside the `object` body.
- Interface fulfillment maps interface signatures to object methods via `interfaceMethod(parameterTypes...) via ObjectName.methodName;`.
- Mapping includes parameter types/signature to support overload resolution.
- The mapped object method must have a compatible signature.
- Constructor overloading is supported by typed signatures.
- Method overloading is supported by typed signatures.
- Object fields must be initialized either:
  - at field declaration, or
  - during constructor execution.
- Current constructor rules (implemented):
  - If an object has fields, it must declare at least one constructor.
  - Constructor overloads resolve by parameter-type signatures with best-match conversion scoring.
  - Each constructor must definitely assign all declared fields via `this.field = ...`.
  - `return` is not currently allowed in constructors.
- Current method lowering (implemented):
  - Methods are lowered to hidden callable bodies with implicit `this` as the first argument.
  - Method resolution uses object type + method name + parameter-type signature with best-match conversion scoring.
  - Methods may use either `function<ReturnType> name(...)` or implicit-void `function name(...)`.
- Reserved field names (currently disallowed): `length`, `hasValue`, `value`, `or`.
- `record` is a type like `object`, but passed by value.
- `object` instances are passed by reference.
- For remaining categories, reference/value behavior follows common C# conventions (provisional).

Interface example:

```code
interface Methodable {
  function<integer> method(string name);
}
```

Object declaration with constructor and method:

```code
object Person {
  string name;

  constructor(string name) {
    this.name = name;
  }

  function<integer> method(string name) {
    print(name); // local variable
    print(this.name); // object field
    return 1;
  }
}
```

Object field initialization example:

```code
object Counter {
  integer count;
  constructor(integer initial) {
    this.count = initial;
  }
}
```

Interface implementation blocks:

```code
implement Methodable for Person {
  method(string name) via Person.method;
}
```

Current limitation:
- Interface dispatch currently covers direct interface-typed values (including locals, parameters, returns, and object fields). Wider integration (arrays/containers of interfaces and package/module boundaries) is still being expanded.

Instantiation:

```code
Person instance = new Person("Ada");
```

## 10. Member Access and Visibility
- Supported access modifiers: `public`, `package`, `private`.
- Default member visibility is `package`.
- `static` members are supported.
- `constant` fields are supported.
- Field access allows both unqualified and `this.`-qualified forms.
- If a local variable shadows a field, unqualified access resolves to the local variable.
- Use `this.fieldName` to reference the shadowed member field.

```code
function method(string name) {
  print(name); // local variable
  print(this.name); // member field
}
```

## 11. Modules and Imports
- Import syntax:
  - `import identifier from "FilePath";`
  - Alias form: `import sourceName as localName from "FilePath";`
  - Grouped/selective form: `import { name1, name2 as alias2 } from "FilePath";`
- String-path imports resolve relative to the file containing the `import`.
- Import resolution order (current implementation): current file directory, then `lib/` folders discovered while walking ancestor directories (including project root `lib/` when present).
- Imported symbol must be exported by the target module.
- Alias imports support exported `function`, `object`, and `interface` declarations.
- Source file extension is `.code`.
- Package declaration syntax: `package Name;` (dot-separated segments allowed).
- Package declaration rules (current implementation):
  - at most one per module;
  - must appear before imports/declarations;
  - package names are currently metadata only (namespace enforcement deferred).
- Package manifest baseline (current implementation):
  - Compiler auto-discovers nearest ancestor `code.package.json` from the entry module.
  - Manifest schema v1 is validated (`schemaVersion`, `name`, `version`, `kind`, `entry`; optional exports/deps/target overrides/host capabilities).
  - Optional `targets` must include selected compile target; optional `hostAbi.requires` capabilities must be valid for the target.
  - Manifest `entry` and target override entry paths must reference existing `.code` files.
  - Declared `dependencies` are resolved from local package folders and must satisfy manifest version ranges (`x.y.z` exact or `^x.y.z` caret).
  - Compiler emits `code.lock.json` in the package root with deterministic target-specific resolution results.

```code
import anotherIdentifier from "FilePath";
import exportedFunction as errorExample from "PathToExampleAbove";
import { add, subtract as minus } from "math.code";
package Example.Package;
```

## 12. Exports
- Exported declarations use `export` before the declaration.
- Multiple exports can exist in one module.
- Export is currently implemented for `function`, `object`, and `interface` declarations.

```code
export function<fallible<real>> exportedFunction(real value) {
  // ...
}
```

## 13. Error Model (Observed)
- `fallible<T>` represents a value that may fail.
- Functions may return `fallible<T>`.
- Built-in `error` shape contains:
  - `type`
  - `message`
  - `stacktrace`
- Call-site error handling uses `on error` (chosen syntax).
- The implicit `error` object is available in `on error` scope.
- When converting `fallible<T>` to `T` via `on error`, the handler must terminate with either `yield <T>` or `panic(...)`.
- In functions returning `fallible<T>`, `on error` may explicitly propagate the current error:
  - `... on error return error;`
- Error transformation is supported:
  - `... on error return new error(string type, string message);`
- Supported handling patterns:
  - `on error { ... }` block form
  - `on error yield fallbackValue`
  - `on error panic("message {error}")`
  - `on error return error`
  - `on error return new error(type, message)`
- Handler flexibility: `on error return <errorExpression>` is valid when the expression type is `error`.
- A fallible result can be kept unhandled by assigning it to `fallible<T>`.
- Stacktrace capture: on `panic` or unhandled `fallible` error, capture frames as `at function (file:line)` plus error `type` and `message`, attached to `error.stacktrace`.

```code
real result1 = errorExample(0) on error {
  print("Error: {error}");
  yield 0;
};

real result2 = errorExample(0) on error yield 0;

fallible<real> pending = errorExample(0);

real result3 = errorExample(0) on error panic("Error message {error}");
```

## 14. Compiler Behavior (current implementation)
- Definite assignment is enforced: a local must be assigned before first read; parameters and `foreach` loop variables are treated as assigned.
- Constants are immutable after initialization (`constant Type name = value;`).
- Compile-time constant folding for literal arithmetic (`+`, `-`, `*`) and string literal concatenation reduces runtime work without changing semantics.
- Runtime errors include line/column mapping and a bytecode call stack derived from embedded debug info in the compiled `.bytecode` file.
- Type syntax is parsed through structured type references (`TypeRef`) including generic forms; named object types resolve through the object symbol table.
- Object symbols are collected before function/body checking, enabling duplicate object/field checks and forward references in object field types.
- Constructor symbols are collected and used for `new Type(...)` arity/type validation.
- Method symbols are collected and used for `obj.method(args)` arity/type validation.
- Interface symbols and `implement` mappings are validated, and interface-typed method calls are lowered to runtime dispatch tables over mapped object methods.
- File compilation performs module linking: recursive import graph load, export-name validation, module-scope import/declaration conflict checks, cycle detection, and flattening to a single bytecode unit.
- Module diagnostics include import-chain context (`entry -> dep1 -> dep2`) for unresolved imports/exports and cycles.
- Compiler tooling includes module graph and linker trace output:
  - `--dump-module-graph [outputPath]` emits entry/modules/import edges for the current compile.
  - Module graph output format supports `text` (default), `json`, and `dot` (Graphviz).
  - Format selection: explicit `--module-graph-format <text|json|dot>` or file extension inference (`.json`, `.dot`, `.gv`).
  - `--trace-linker` prints linker visit/resolve/link steps.
- Compile target selection is available via `--target vm-native|vm-web` (default: `vm-native`).
- Target capability validation (current baseline):
  - Compiler infers capability groups from module package/import namespaces (`std.*`, `engine.*`) and from host-lowered language features (`print`, time intrinsics, native-only host intrinsics, and engine host stubs).
  - Compiler merges inferred capabilities with manifest-declared `hostAbi.requires`.
  - Build fails if the selected target does not support an inferred capability (e.g., `std.fs` on `vm-web`).
  - Module graph outputs include `target` and inferred `requiredCapabilities`.
- Dependency lockfile behavior (current baseline):
  - If a manifest exists, compile resolves `dependencies` and writes/updates `code.lock.json`.
  - Lockfile includes `schemaVersion`, selected `target`, and package entries (`name`, `version`, `resolved`, `integrity`).
  - If a package has a matching validated `.codelib` artifact, lockfile `resolved` points to that artifact instead of the raw manifest path.
- Library artifact behavior (current baseline):
  - Packages with `kind: "library"` emit `<package>-<version>-<target>.codelib` in package root on compile.
  - Artifact contains package identity, target, entry path, export map, required capabilities, and embedded bytecode payload.
  - CLI accepts `.codelib` as executable/disassemblable input.
- Time intrinsics (current baseline):
  - `unix_ms() -> integer`
  - `unix_us() -> integer`
  - `mono_ns() -> integer` (monotonic process-relative nanoseconds)
  - `mono_ticks() -> integer`
  - `mono_ticks_per_second() -> integer`
  - `sleep_ms(integer ms) -> void` (native-only host API; compile-time target check rejects it for `vm-web`)
  - `read_line() -> string` (native-only host API; compile-time target check rejects it for `vm-web`)
  - These currently lower through host ABI symbols (`std.time.*`) rather than dedicated language-level stdlib modules.
- Print lowering (current baseline):
  - `print(expr);` lowers through host ABI symbol `std.io.print`.
  - VM host binding mismatch (missing symbol/arity) raises `HostBindingError` at runtime.
  - Native-only host calls include target-specific runtime diagnostics when executed on `vm-web` host tables.
  - Engine-facing host stubs are available through intrinsics:
    - `window_create(...)`, `window_should_close(...)`, `window_present(...)`
    - `input_key_down(...)`
    - `gfx_clear(...)`, `gfx_draw_rect(...)`
    - Current runtime behavior for these engine intrinsics is prototype/no-op on both native and web hosts.
  - Note: high-range timing values may eventually need dedicated 64-bit numeric/value support for full precision guarantees.
- Object construction and field access lower to dedicated VM opcodes (`NEW_OBJECT`, `GET_FIELD`, `SET_FIELD`).
- Arrays: literals `{...}` create arrays; typed declarations `array<integer> xs = {1,2,3};`; dynamic `new array<integer>(n)` requires a size; `xs.length` yields length; `foreach` iterates arrays by element.

```code
function<fallible<real>> run(string input, real count) {
  real parsed = parseReal(input) on error return error;
  return divide(parsed, count);
}
```

```code
function<fallible<real>> runWithTypedError(string input, real count) {
  real parsed = parseReal(input)
    on error return new error("ParseError", "Could not parse '{input}'");
  return divide(parsed, count);
}
```

## 14. Comments
- Single-line comments:

```code
// comment
```

- Block comments (inline or multi-line):

```code
/* comment */
```

## 15. Strings (Observed)
- Supports quoted string literals: `"text"`.
- Supports interpolation markers with braces inside string literals, e.g.:
  - `"usage: {arguments[0]} <your name>"`
  - `"hello {arguments[1]}"`
- Interpolation rules:
  - Any expression valid in expression position may appear inside `{ ... }`.
  - Escape literal braces with `\{` and `\}`.
  - Nested string literals inside an interpolation are disallowed to keep parsing simple.

## 16. Literals
- Numeric: see numeric literal rules above.
- Boolean: `true` and `false`.
- Strings: quoted `"text"`; interpolation with `{ ... }` allowed (escape braces with `\{` and `\}`); supports concatenation via `+`. Interpolation currently supports expressions.

## 17. Open Questions
- Future package search paths beyond project `lib/` (configuration format, stdlib layout).
