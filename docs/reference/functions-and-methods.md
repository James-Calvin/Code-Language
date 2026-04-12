# Functions and Methods

## Function Syntax

Canonical return syntax:

```code
function<integer> add(integer left, integer right) {
  return left + right;
}
```

Void functions may omit `<void>`:

```code
function say_hello(string name) {
  print("hello {name}");
}
```

Explicit void is also accepted:

```code
function<void> say_done() {
  print("done");
}
```

The parser also accepts a leading return type before the function name, but the reference uses the angle-bracket form for consistency.

Common mistakes:

- Parameters should be typed.
- Non-void functions must return a compatible value.
- `return;` is only for void functions and early exits.

## Calls

```code
function<integer> triple(integer value) {
  return value * 3;
}

print(triple(4));
```

Output:

```text
12
```

Arguments are checked by type. Numeric widening is allowed when it is lossless according to the current type ranks.

## Overloads

Functions, methods, and constructors resolve by typed signature.

```code
function<integer> value(integer x) {
  return x;
}

function<real> value(real x) {
  return x;
}

print(value(3));
```

Output:

```text
3
```

Resolution:

1. Prefer exact parameter type matches.
2. Otherwise choose the candidate with the lowest conversion cost.
3. If two candidates tie, compilation fails with an ambiguity error.

Common mistakes:

- Declaration order does not break overload ties.
- There are no variadic functions today.

## Methods

Methods are functions inside `object` or `record` declarations.

```code
object Counter {
  integer count;

  constructor(integer initial) {
    count = initial;
  }

  function add(integer amount) {
    count += amount;
  }

  function<integer> read() {
    return count;
  }
}

Counter counter = new Counter(2);
counter.add(3);
print(counter.read());
```

Output:

```text
5
```

Method behavior:

- Methods receive an implicit `this`.
- Inside an object or record method, unshadowed field names resolve to `this.field`.
- Bare method calls try the current object before top-level functions.
- If a local or parameter shadows a field, use `this.field`.

Common mistakes:

- A method call target must be an object, record, interface, or built-in collection.
- Private or package members can fail access checks even when the type is visible.

## Constructors

Constructors initialize object and record fields.

```code
object Player {
  string name;
  integer score;

  constructor(string name) {
    this.name = name;
    score = 0;
  }
}
```

Constructor rules:

- Objects and records with fields must define constructors.
- Each constructor must assign every field.
- Constructor overloads resolve by parameter types.
- Constructors cannot return.

Common mistakes:

- Field initialization can use `this.field = value;` or unshadowed `field = value;`.
- If a constructor parameter has the same name as a field, use `this.field`.

## Main

Console apps can define `main` when they need command-line arguments:

```code
function main(array<string> arguments) {
  if arguments.length < 2 then {
    print("usage: {arguments[0]} <name>");
    return;
  }

  print("hello {arguments[1]}");
}
```

Common mistakes:

- Top-level statements are fine for simple programs.
- The first argument is the executable name when `main` is used.
