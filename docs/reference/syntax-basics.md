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

## Comments

```code
// single-line comment

/* block comment */
```

Block comments can span lines.

## Identifiers

Identifiers start with a letter or `_`, then may contain letters, digits, and `_`.

```code
integer player_score = 5;
integer _temporary2 = player_score + 1;
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
constant integer max_lives = 3;
```

Constants must be initialized and cannot be reassigned.

Common mistakes:

- `constant integer max;` is rejected because constants must have initializers.
- `max_lives = 4;` is rejected because constants are immutable.
- A local without an initializer must be assigned before first read.

## Literals

Implemented literal forms:

| Form | Example | Type |
| --- | --- | --- |
| Integer number | `42`, `0b1010`, `0o17`, `0x1f` | `integer` |
| Boolean | `true`, `false` | `boolean` |
| String | `"hello"` | `string` |
| None | `none` | `optional<T>` value |
| Array literal | `{1, 2, 3}` | `array<T>` |

Numeric literals are currently integer literals. Decimal, binary (`0b`), octal (`0o`), and hexadecimal (`0x`) forms are supported. Real-valued results come from numeric operations and functions, for example division and math intrinsics.

Common mistakes:

- Decimal point literals such as `1.5` and numeric suffixes such as `i32` are draft-only today.
- Assigning an integer literal directly to `whole` is not the current recommended pattern because there is no unsigned literal syntax yet.

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
| Relational | `<`, `<=`, `>`, `>=` |
| Equality | `==`, `!=` |
| Logical and | `and` |
| Logical or | `or` |
| Assignment | `=`, `+=`, `-=`, `*=`, `/=`, `%=` |

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
