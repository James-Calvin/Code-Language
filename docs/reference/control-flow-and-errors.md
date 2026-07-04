# Control Flow and Errors

## If and Else

Syntax:

```code
if condition then statement
if condition then { statements } else { statements }
```

Example:

```code
integer score = 12;
if score >= 10 then {
  print("win");
} else {
  print("try again");
}
```

Output:

```text
win
```

Single-statement form:

```code
if score > 0 then print("positive");
```

Common mistakes:

- `then` is required.
- Conditions must be boolean in `if` and `while`.

## Switch

Syntax:

```code
switch value then {
  case expression then statement
  default then statement
}
```

Example:

```code
enum Direction {
  Left;
  Right;
}

Direction direction = Direction.Left;
switch direction then {
  case Direction.Left then print("left");
  case Direction.Right then print("right");
  default then print("unknown");
}
```

Output:

```text
left
```

Behavior:

- The switch value is evaluated once.
- Cases are checked in order.
- There is no fallthrough.
- `default` is optional, but must be last when present.

Common mistakes:

- A `switch` must contain at least one `case` or `default`.
- In an `on error` handler, a `switch` used to choose a `yield` fallback usually needs a `default` branch because enum exhaustiveness is not implemented yet.

## While

```code
integer count = 3;
while count > 0 then {
  print(count);
  count -= 1;
}
```

Output:

```text
3
2
1
```

`break` exits the nearest enclosing loop, and `continue` jumps to the next loop iteration:

```code
integer count = 0;
while count < 5 then {
  count += 1;
  if count == 2 then continue;
  if count == 4 then break;
  print(count);
}
```

Output:

```text
1
3
```

## For

Syntax:

```code
for initializer; condition; increment then statement
```

Example:

```code
integer total = 0;
for integer i = 0; i < 4; i++ then {
  total += i;
}
print(total);
```

Output:

```text
6
```

Common mistakes:

- `then` is required after the increment expression.
- The initializer may declare a typed local or be an expression statement.

## Foreach

Numeric `foreach` iterates integers from `0` through `count - 1`:

```code
integer total = 0;
foreach i in 4 then {
  total += i;
}
print(total);
```

Output:

```text
6
```

Array `foreach` iterates elements:

```code
array<integer> numbers = {2, 4, 6};
foreach number in numbers then {
  print(number);
}
```

Output:

```text
2
4
6
```

Common mistakes:

- `foreach` does not currently iterate `map`, `set`, `queue`, or `stack`; the planned map form should yield entry values rather than keys or values alone.
- The loop variable is created by the loop; do not declare its type in the loop header.

## Return

Return a value from a typed function:

```code
function<integer> add(integer left, integer right) {
  return left + right;
}
```

Return from a void function:

```code
function say_hi() {
  print("hi");
  return;
}
```

Common mistakes:

- Non-void functions must return a value on all checked paths.
- Constructors cannot return.

## Panic

`panic` is for unrecoverable failures.

```code
integer lives = 0;
if lives <= 0 then panic("no lives left");
```

Behavior:

- Raises a `UserError`.
- Runtime diagnostics include source file, line, column, phase, and a call stack when debug data is available.

Common mistakes:

- Use `fallible<Value, ErrorCode>` for expected recoverable failures.
- Use `panic` for bugs or impossible states.

## Runtime Error Diagnostics

Generated web apps and native runs use bytecode debug metadata to report where a runtime error happened in Code source when that metadata is available.

Example shape:

```text
Runtime error during module initialization: Map key not found at blacksmithing.code:36:12
  at blacksmithing.code:36:12
```

Diagnostic coverage includes common runtime failures such as missing map keys, array index errors, empty queue/stack access, optional `none` unwraps, and type expectation failures. If source metadata is missing, the runtime falls back to the bytecode instruction pointer.

## Recoverable Errors

Return success from a fallible function:

```code
enum LoadError {
  Missing;
  Invalid;
}

function<fallible<integer, LoadError>> load_count(boolean ok) {
  if ok then return 5;
  return error(LoadError.Missing, "missing count");
}
```

Handle failure:

```code
integer count = load_count(false) on error {
  print(error.message);
  yield 0;
};

print(count);
```

Output:

```text
missing count
0
```

For quick prototypes, `fallible<Value>` defaults the error-code type to `integer`, and `error("message")` creates an error with code `0`:

```code
function<fallible<integer>> quick_count(boolean ok) {
  if ok then return 5;
  return error("missing count");
}

integer count = quick_count(false) on error {
  print(error.code);
  print(error.message);
  yield 0;
};
```

Inside the handler:

| Name | Type | Use |
| --- | --- | --- |
| `error.code` | declared error-code type | Branch on failure category |
| `error.message` | `string` | Human-readable detail |
| `yield value;` | success value type | Supplies fallback value |
| `return ...;` | enclosing function return type | Exits the enclosing function |
| `panic(...);` | no return | Escalates unrecoverable failure |

Common mistakes:

- The handler must `yield`, `return`, or `panic`.
- `yield` only works inside an `on error` handler.
- The yielded value must match the fallible success type.
- `error("message")` is only available for `fallible<Value>` or `fallible<Value, integer>`; enum-coded fallible functions must return `error(EnumName.Member)` or `error(EnumName.Member, message)`.
