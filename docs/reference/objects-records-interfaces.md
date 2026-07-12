# Objects, Records, and Interfaces

## Generic Types

Objects, records, and interfaces may declare type parameters after their names, for example `object Registry<Data>`, and are used through closed types such as `Registry<Position>`. Type parameters may appear recursively in fields, constructors, methods, and inline or external interface implementations. Construct explicitly with `new Registry<Position>()`.

Each closed type is invariant and independently specialized at compile time. Static storage is separate per object or record specialization. Generic functions, methods, constraints, variance, inferred/default type arguments, and bare open-generic values are not supported in V1.

## Objects

Objects are reference-like runtime instances.

```code
object Person {
  string name = "unknown";
  integer visits = 0;

  constructor(string name) {
    this.name = name;
  }

  function greet() {
    visits += 1;
    print("hello {name}");
  }
}

Person person = new Person("Ada");
person.greet();
```

Output:

```text
hello Ada
```

Behavior:

- Use `new Type(...)` or `Type(...)` to construct. The shorthand only applies when a normal function call does not exist.
- Fields are read and written with `object.field`.
- Fields may declare defaults with `Type name = expression;`.
- Defaults run for each new instance before the constructor body.
- Fields without defaults must still be definitely assigned by every constructor.
- Methods are called with `object.method(...)`.
- Object variables refer to runtime instances.
- Primary constructor syntax is supported when constructor parameters match same-named fields, for example `object Player(string name) { string name; }`.

Common mistakes:

- Field names `length`, `hasValue`, `value`, and `or` are reserved.
- Field defaults cannot read constructor parameters, `this`, or other fields. Use a constructor when one field depends on another.
- Objects with fields that lack defaults need constructors that assign every non-defaulted field.
- `TypeName.method(...)` is static member access, not construction. Write `TypeName().method(...)` when chaining from a zero-argument builder instance.

## Static Members

Objects and records may declare static fields and methods. Static members belong
to the type, not to each runtime instance.

```code
object Counter {
  static private integer nextId = 0;
  static public constant integer maxCount = 10;

  static public function<integer> next() {
    nextId += 1;
    return nextId;
  }
}

print(Counter.next());
print(Counter.maxCount);
```

Static member rules:

- Write `static` before optional visibility, for example `static private integer nextId = 0;`.
- Static fields initialize once during module initialization.
- Static fields may be mutable or `constant`; static constants must have initializers.
- Static methods do not receive `this`.
- Inside the declaring object/record, unshadowed bare names may resolve to same-type static fields and same-type static methods.
- From outside the declaring object/record, access static members through the type: `TypeName.member`.
- Accessing a static member through an instance is a compile-time error.
- Static members do not satisfy interface requirements.
- Static fields on records are not part of record equality, hashing, copying, or value semantics.

## Records

Records are copy-by-value helper data types.

```code
record Point {
  integer x = 0;
  integer y = 0;

  constructor(integer x, integer y) {
    this.x = x;
    this.y = y;
  }

  function<Point> moved(integer amount) {
    x += amount;
    y += amount;
    return this;
  }
}

Point first = new Point(1, 2);
Point second = first.moved(5);
print(first.x);
print(second.x);
```

Output:

```text
1
6
```

Record behavior:

- Records copy on assignment, parameter passing, returns, and collection insertion.
- Records without explicit constructors get an implicit field-order constructor for fields that lack defaults.
- Record construction can use `new Type(...)` or `Type(...)`.
- Record methods receive a copied `this`.
- Persistent updates should return a record and assign it at the call site.
- Hashable records support structural equality and may be used as `map` keys or `set` elements.
- By default, every record field participates in equality and hashing.
- Use contextual `key` fields when only selected fields define identity.
- Use contextual `ignore key` fields when a payload field should not define identity.
- See [Record Equality and Hashing](record-equality-and-hashing.md) for the full rules.

Participating hash fields may contain:

- `whole`, `integer`, `real`, `boolean`, `string`
- enums
- object references, by identity
- hashable records
- `optional<T>` where `T` is hashable
- collections whose contained types are hashable

Common mistakes:

- Mutating fields inside a record method changes the copied receiver, not the original caller value.
- Participating fields with interface or fallible types are not hashable in V1; use `key` or `ignore key` to define the hashable identity.
- `key` record fields are constructor-only after initialization.
- A record cannot mix `key` and `ignore key` fields.

## Interfaces

Interfaces define field and method contracts.

```code
interface Reader {
  integer count;
  function<integer> read();
}
```

### External Implementation

```code
object Counter {
  integer count;

  constructor(integer value) {
    count = value;
  }

  function<integer> read_value() {
    return count;
  }
}

implement Reader for Counter {
  read() via Counter.read_value;
}

Reader reader = new Counter(7);
reader.count += 1;
print(reader.count);
print(reader.read());
```

Output:

```text
8
8
```

### Data-Only Interfaces

Interfaces may declare only fields. They still require explicit implementation,
but the implement block can be empty because there are no methods to map.

```code
interface Ingredient {
  string name;
  integer quantity;
}

object MetalBar {
  string name;
  integer quantity;

  constructor(string materialName, integer startingQuantity) {
    name = materialName;
    quantity = startingQuantity;
  }
}

implement Ingredient for MetalBar {
}

Ingredient iron = new MetalBar("iron", 2);
iron.quantity += 3;
print(iron.name);
print(iron.quantity);
```

### Inline Implementation

```code
object One {
  constructor() {
  }

  implement Reader.read() {
    return 1;
  }
}

Reader reader = new One();
print(reader.read());
```

Output:

```text
1
```

Interface behavior:

- Interface fields use `Type name;` and are public read/write contract requirements.
- Interface methods must declare explicit return types.
- Objects and records can implement interfaces.
- Interface-typed locals, parameters, returns, fields, and arrays are supported.
- Interface field reads and writes use the interface contract and mutate the concrete object's field.
- Interface method calls dispatch at runtime.
- Inline `implement Interface.method(...)` inherits the return type from the interface method.
- External `implement Interface for Type` maps interface signatures to concrete methods with `via`.
- Empty `implement Interface for Type {}` blocks are valid when all requirements are fields.

Common mistakes:

- Interface fields cannot declare initializers, constants, or visibility modifiers.
- Concrete fields that satisfy interface fields must be public and have the same type.
- External implementation maps by method name and parameter type signature.
- The mapped method return type must satisfy the interface return type.
- A non-implementing object cannot be assigned to an interface-typed variable.

## Visibility

Top-level declarations and object/record members can use:

| Modifier | Meaning |
| --- | --- |
| `public` | Accessible from any importing module |
| `package` | Accessible only from modules with the same `package Name;` |
| `private` | Top-level: module-local. Member: declaring-type local |
| `export` | Legacy compatibility alias for top-level `public` |

Member example:

```code
package Example;

public object Meter {
  private integer count;

  public constructor() {
    count = 0;
  }

  public function<integer> read() {
    return count;
  }
}
```

Private members are accessible from code inside the declaring type, including through another instance of the same type. Package members require matching package names.

Common mistakes:

- Package-visible members require the declaring type's module to have a package declaration.
- Interface dispatch can call through the public interface contract, but a non-public mapped method does not become directly callable by name from outside its visibility boundary.
