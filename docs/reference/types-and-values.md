# Types and Values

## Primitive Types

| Type | Use |
| --- | --- |
| `integer` | Signed whole-number values. Decimal and base-prefixed integer literals start here. |
| `whole` | Unsigned whole-number values. Used by some host APIs, such as window handles. |
| `real` | Real-number values. Decimal-point literals and integer values can assign to `real`. |
| `boolean` | `true` or `false`. Prints as `1` or `0`. |
| `string` | Text values. Supports interpolation and concatenation. |
| `void` | No returned value. Used in function and interface signatures. |

Example:

```code
integer lives = 3;
real half = 1 / 2;
boolean alive = lives > 0;
string label = "lives={lives}";
print(label);
print(alive);
print(half);
```

Common mistakes:

- Integer literals may use decimal, binary (`0b`), octal (`0o`), or hexadecimal (`0x`) notation. Real literals use decimal dot forms such as `1.5`, `1.`, or `.5`.
- Exponent notation and numeric literal suffixes are not implemented yet.
- Explicit casts are limited to numeric types and enum/integer conversions: `value as integer`, `value as whole`, `value as real`, `EnumName.Member as integer`, and `integer_value as EnumName`.
- `real as integer` and `real as whole` truncate toward zero; `as whole` rejects negative runtime values.

## Arrays

Syntax:

```code
array<integer> numbers = {1, 2, 3};
array<integer> empty = new array<integer>(0);
```

Array operations:

| Operation | Inputs | Returns or behavior |
| --- | --- | --- |
| `items.length` | none | `integer` length |
| `items[index]` | numeric index | element value |
| `items[index] = value` | numeric index, element value | updates the element |
| `items.append(value)` | element value | appends one item |
| `items.remove_at(index)` | numeric index | removes the item |

Example:

```code
array<integer> items = {10, 20};
items.append(30);
items[1] = 25;
print(items.length);
print(items[1]);
```

Output:

```text
3
25
```

Common mistakes:

- Array indexes must be in range at runtime.
- Empty array literals default to `array<integer>` unless a declaration gives the target type.

## Built-In Collections

Map:

```code
map<string, integer> scores = new map<string, integer>();
scores["coins"] = 10;
scores["coins"] += 5;
print(scores.contains("coins"));
print(scores["coins"]);
```

Output:

```text
1
15
```

Set:

```code
set<string> tags = new set<string>();
tags.add("web");
tags.add("web");
print(tags.length);
print(tags.contains("web"));
```

Output:

```text
1
1
```

Queue:

```code
queue<integer> turns = new queue<integer>();
turns.enqueue(3);
turns.enqueue(5);
print(turns.peek());
print(turns.dequeue());
```

Output:

```text
3
3
```

Stack:

```code
stack<string> history = new stack<string>();
history.push("start");
history.push("play");
print(history.peek());
print(history.pop());
```

Output:

```text
play
play
```

Collection methods:

| Type | Operation | Inputs | Returns or behavior |
| --- | --- | --- | --- |
| `map<K, V>` | `items[key]` | `K` | `V` |
| `map<K, V>` | `items[key] = value` | `K`, `V` | stores value |
| `map<K, V>` | `contains(key)` | `K` | `boolean` |
| `map<K, V>` | `remove(key)` | `K` | removes key |
| `set<T>` | `add(value)` | `T` | adds value |
| `set<T>` | `contains(value)` | `T` | `boolean` |
| `set<T>` | `remove(value)` | `T` | removes value |
| `queue<T>` | `enqueue(value)` | `T` | appends to back |
| `queue<T>` | `dequeue()` | none | removes and returns front |
| `queue<T>` | `peek()` | none | returns front |
| `stack<T>` | `push(value)` | `T` | pushes value |
| `stack<T>` | `pop()` | none | removes and returns top |
| `stack<T>` | `peek()` | none | returns top |

All built-in collections expose `.length`.

Common mistakes:

- `foreach` currently supports numeric counts and arrays, not `map`, `set`, `queue`, or `stack`; planned map iteration should yield entry values.
- Reading a missing map key raises a runtime error.
- `dequeue`, `pop`, and `peek` on an empty queue or stack raise runtime errors.

## Optionals

Syntax:

```code
optional<integer> maybe_count = none;
print(maybe_count.hasValue);
print(maybe_count.or(42));
```

Output:

```text
0
42
```

A present optional can be initialized from a plain value:

```code
optional<integer> actual = 7;
if actual.hasValue then {
  print(actual.value);
}
```

Output:

```text
7
```

Optional operations:

| Operation | Returns |
| --- | --- |
| `none` | empty optional value |
| `maybe.hasValue` | `boolean` |
| `maybe.value` | contained value, or panic if empty |
| `maybe.or(fallback)` | contained value or fallback |

Common mistakes:

- `.value` on `none` panics at runtime.
- Use `.or(fallback)` when an empty optional should become a default value.

## Fallible Values

`fallible<Value, ErrorCode>` represents expected recoverable failure. `ErrorCode` must be an enum type or `integer`. `fallible<Value>` is shorthand for `fallible<Value, integer>` for rapid prototyping.

```code
enum ParseError {
  Empty;
  Invalid;
}

function<fallible<integer, ParseError>> parse_count(string text) {
  if text == "" then return error(ParseError.Empty, "empty");
  if text == "one" then return 1;
  return error(ParseError.Invalid, "expected one");
}

integer count = parse_count("") on error {
  print(error.message);
  yield 0;
};

print(count);
```

Output:

```text
empty
0
```

Prototype-friendly integer-coded errors:

```code
function<fallible<integer>> quick_count() {
  return error("empty");
}

integer quick = quick_count() on error {
  print(error.code);
  print(error.message);
  yield 0;
};
```

Common mistakes:

- `error(message)` is only valid for `fallible<Value>` or `fallible<Value, integer>` and uses error code `0`.
- Enum-coded fallible functions must use `error(code)` or `error(code, message)`.
- `fallible<void, E>` is draft-only today.
- There is no propagation shorthand yet; handle errors with `on error`.

## Enumerations

Syntax:

```code
enum Difficulty {
  Easy;
  Normal = 5;
  Hard;
}

Difficulty difficulty = Difficulty.Easy;
if difficulty == Difficulty.Easy then {
  print("easy");
}
```

Output:

```text
easy
```

Behavior:

- Enum members are accessed as `EnumName.Member`.
- Enum values are strongly typed.
- Explicit member values must be integer literals.
- `EnumName.Member as integer` returns the backing integer value.
- `integer_value as EnumName` casts an integer to an enum; literal integer casts must match a declared member value.
- Members after an explicit value continue from that value.

Common mistakes:

- Enum values are not plain integers for assignment or equality.
- `Difficulty.Easy = Difficulty.Hard;` is invalid because enum members are constants.
- Enum casts to `whole` or `real` require an explicit intermediate integer cast.

## Object, Record, and Interface Types

Objects are reference-like runtime instances. Records are copy-by-value data types. Interfaces are contracts for dispatch.

Short example:

```code
interface Reader {
  function<integer> read();
}

record Counter {
  integer value;

  constructor(integer value) {
    this.value = value;
  }

  implement Reader.read() {
    return value;
  }
}

Reader reader = new Counter(5);
print(reader.read());
```

Output:

```text
5
```

See [Objects, Records, and Interfaces](objects-records-interfaces.md) for the full object model.
