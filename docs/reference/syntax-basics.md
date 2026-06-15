# Syntax Basics

## Source Files

Code source files use the `.code` extension. A file may contain top-level statements:

```code
integer score = 10;
print(score);
```

Output:

```text
10
```

Console programs may also define `main`:

```code
function main(array<string> arguments) {
  print("argument count: {arguments.length}");
}
```

Common mistakes:

- Semicolons are required after declarations, expressions, `print`, `return`, `panic`, and `yield`.
- Top-level code runs without `main`; add `main` only when you need command-line arguments.
- Top-level variable and constant declarations are same-module globals. Functions and object/record code in the same file can read them by bare name.

## Comments

```code
// single-line comment

/* block comment */
```

Block comments can span lines.

## Identifiers

Identifiers start with a letter or `_`, then may contain letters, digits, and `_`.

```code
integer playerScore = 5;
integer temporary2 = playerScore + 1;
```

Do not use language keywords as names. Current keywords include:

```text
integer whole real boolean void optional fallible array object record interface enum
constant if then else switch case default while break continue return print function constructor
implement via import export from as package public private and or not for foreach in
new panic error on yield none true false
```

`string`, `map`, `set`, `queue`, and `stack` are built-in type names even though they are parsed through the identifier path.

## Declarations

Mutable local:

```code
integer count = 0;
count = count + 1;
```

Constant:

```code
constant integer maxLives = 3;
```

Constants must be initialized and cannot be reassigned.

Module globals:

```code
constant real turn = tau;
integer updateCount = 0;

function update() {
  updateCount++;
  print(turn);
}
```

Bare-name resolution checks locals and parameters first, then implicit `this` fields inside object/record bodies, then same-module globals, then built-in constants such as `pi` and `tau`.

Common mistakes:

- `constant integer max;` is rejected because constants must have initializers.
- `maxLives = 4;` is rejected because constants are immutable.
- A local without an initializer must be assigned before first read.
- Same-module globals are not imported or exported as public module API yet.

## Literals

Implemented literal forms:

| Form | Example | Type |
| --- | --- | --- |
| Integer number | `42`, `0b1010`, `0o17`, `0x1f` | `integer` |
| Real number | `1.5`, `1.`, `.5` | `real` |
| Boolean | `true`, `false` | `boolean` |
| String | `"hello"` | `string` |
| None | `none` | `optional<T>` value |
| Array literal | `{1, 2, 3}` | `array<T>` |

Numeric literals support decimal integers, binary (`0b`), octal (`0o`), hexadecimal (`0x`), and decimal real forms with a dot. Unsuffixed integer literals can cover the implemented `integer32` / `whole32` range. Exponent notation and numeric suffixes are not implemented yet.

Common mistakes:

- Exponent literals such as `1e3` and numeric suffixes such as `i32` are draft-only today.
- Use `byte`, `whole8`, `whole16`, `whole32`, `integer8`, `integer16`, `integer32`, `real32`, and `real64` as type names when you want checked storage boundaries. `byte` is exactly `whole8`; `real64` is exactly `real`.
- Dynamic narrowing to a sized type requires an explicit cast, for example `value as byte`.

## Expressions and Operators

Arithmetic:

```code
integer value = (2 + 3) * 4;
print(value % 3);
```

Output:

```text
2
```

Comparison and equality:

```code
print(3 < 4);
print("a" == "a");
```

Output:

```text
1
1
```

Logical operators short-circuit:

```code
boolean ready = true;
boolean blocked = false;
if ready and not blocked then print("go");
```

Output:

```text
go
```

Operator precedence, high to low:

| Level | Operators |
| --- | --- |
| Prefix | `+x`, `-x`, `not x` |
| Multiplicative | `*`, `/`, `%` |
| Additive | `+`, `-` |
| Cast | `value as Type` |
| Relational | `<`, `<=`, `>`, `>=` |
| Equality | `==`, `!=` |
| Logical and | `and` |
| Logical or | `or` |
| Assignment | `=`, `+=`, `-=`, `*=`, `/=`, `%=` |

Cast examples:

```code
print(3.8 as integer);
print(3 as real);
print(255 as byte);
```

Output:

```text
3
3
255
```

Postfix increment/decrement:

```code
integer count = 1;
count++;
print(count);
count--;
print(count);
```

Output:

```text
2
1
```

Common mistakes:

- `!condition` is not supported; use `not condition`.
- `&&` and `||` are not supported; use `and` and `or`.
- Bitwise operators are not part of the current language.
- Casts are intentionally limited to numeric types and enum/integer conversions today.
- Sized numeric casts and sized storage boundaries perform runtime range checks for dynamic values.

## Strings and Interpolation

Strings use double quotes:

```code
string name = "Ada";
print("hello {name}");
```

Output:

```text
hello Ada
```

Interpolation accepts expressions:

```code
integer a = 2;
integer b = 3;
print("sum={a + b}");
```

Output:

```text
sum=5
```

String concatenation uses `+`:

```code
print("score: " + 10);
```

Output:

```text
score: 10
```

Common mistakes:

- Escape literal interpolation braces as `\{` and `\}`.
- Interpolation is parsed as an expression, so unmatched braces cause a compile error.
