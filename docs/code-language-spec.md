# Code Language Specification (Living Draft)

Version: 0.8  
Last updated: 2026-02-08

## 1. Goals and Design
- `Code` is a general-purpose language.
- It prioritizes clarity over brevity.
- Syntax should be consistent and intuitive.
- It targets new developers while teaching concepts transferable to other languages.
- It supports object-oriented programming.
- It does not use class inheritance.
- It uses interfaces for structural contracts.
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
- Type conversions are generally explicit.
- Lossless numeric promotions are allowed by convention.

Examples:

```code
integer value = 0;
value += 1;
whole count = 0;
constant real PI = 3.14159;
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

```code
foreach number in numbers then {
  if number < 0 then break;
  if isPrime(number) then continue;
}
```

- Logical conjunction uses `and`:

```code
if a > 0 and b > 0 then {
  // branch
}
```

- Operator precedence and associativity follow conventional modern-language behavior.
- A formal precedence table is deferred.

## 8. Collections (Observed)
- Generic array type syntax: `array<Type>`.
- Array literal syntax uses braces.
- Arrays can be allocated with `new array<Type>(size)`.

```code
array<integer> numbers = {1, 1, 2, 3, 5, 8, 13};
array<integer> otherNumbers = new array<integer>(10);
```

## 9. Object Model and Interfaces
- No inheritance.
- Contracts are declared as `interface`.
- Concrete types are declared as `object`.
- Objects can implement multiple interfaces.
- `implement Interface for Object` is required for interface fulfillment.
- Methods may also be declared directly inside the `object` body.
- Interface fulfillment maps interface signatures to object methods via `interfaceMethod(parameterTypes...) via ObjectName.methodName;`.
- Mapping includes parameter types/signature to support overload resolution.
- The mapped object method must have a compatible signature.
- Constructor overloading is supported.
- Object fields must be initialized either:
  - at field declaration, or
  - during constructor execution.
- `record` is a type like `object`, but passed by value.
- `object` instances are passed by reference.
- For remaining categories, reference/value behavior follows common C# conventions (provisional).

Interface example:

```code
interface Methodable {
  function method(string name);
}
```

Object declaration with constructor and method:

```code
object Person implements Methodable {
  string name;

  constructor(string name) {
    this.name = name;
  }

  function method(string name) {
    print(name); // local variable
    print(this.name); // object field
  }
}
```

Object field default example:

```code
object Counter {
  integer count = 0;
}
```

Interface implementation blocks:

```code
implement Methodable for Person {
  method(string name) via Person.method;
}
```

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
  - `import identifier from RuntimeLibrary;`
  - `import identifier from "FilePath";`
  - Alias form: `import sourceName as localName from "FilePath";`
- `RuntimeLibrary`-style identifiers represent built-in package namespaces.
- String-path imports resolve relative to the file containing the `import`.
- Source file extension is `.code`.
- Package declaration syntax: `package Name;`.
- Conventional ordering places `package Name;` immediately after imports.

```code
import identifier from RuntimeLibrary;
import anotherIdentifier from "FilePath";
import exportedFunction as errorExample from "PathToExampleAbove";
package ExamplePackage;
```

## 12. Exports
- Exported declarations use `export` before the declaration.
- Multiple exports can exist in one module.

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
- A fallible result can be kept unhandled by assigning it to `fallible<T>`.

```code
real result1 = errorExample(0) on error {
  print("Error: {error}");
  yield 0;
};

real result2 = errorExample(0) on error yield 0;

fallible<real> pending = errorExample(0);

real result3 = errorExample(0) on error panic("Error message {error}");
```

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

## 16. Open Questions
- Exact interpolation grammar (expressions allowed vs identifiers only).
- Exact numeric literal rules (digit separators, bases, suffixes).
- Exact cast syntax and the precise lossless-promotion matrix.
- Exact overload tie-breaker rules when multiple overloads are otherwise compatible.
- Optional value access after `hasValue` check (property vs unwrap syntax and flow narrowing details).
- Module/package lookup details beyond `.code` extension and relative paths.
- Exact `stacktrace` capture semantics and formatting.
