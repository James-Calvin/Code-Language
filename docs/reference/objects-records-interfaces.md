# Objects, Records, and Interfaces

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

- Use `new Type(...)` to construct.
- Fields are read and written with `object.field`.
- Fields may declare defaults with `Type name = expression;`.
- Defaults run for each new instance before the constructor body.
- Fields without defaults must still be definitely assigned by every constructor.
- Methods are called with `object.method(...)`.
- Object variables refer to runtime instances.

Common mistakes:

- Field names `length`, `hasValue`, `value`, and `or` are reserved.
- Field defaults cannot read constructor parameters, `this`, or other fields. Use a constructor when one field depends on another.
- Objects with fields that lack defaults need constructors that assign every non-defaulted field.

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
- Record methods receive a copied `this`.
- Persistent updates should return a record and assign it at the call site.
- Hashable records support structural equality and may be used as `map` keys or `set` elements.

Hashable record fields may contain:

- `whole`, `integer`, `real`, `boolean`, `string`
- enums
- hashable records
- `optional<T>` where `T` is hashable

Common mistakes:

- Mutating fields inside a record method changes the copied receiver, not the original caller value.
- Records with non-hashable fields still work as values, but cannot use structural equality or serve as map keys or set elements.

## Interfaces

Interfaces define method contracts.

```code
interface Reader {
  function<integer> read();
}
```

### External Implementation

```code
object Counter {
  integer value;

  constructor(integer value) {
    this.value = value;
  }

  function<integer> read_value() {
    return value;
  }
}

implement Reader for Counter {
  read() via Counter.read_value;
}

Reader reader = new Counter(7);
print(reader.read());
```

Output:

```text
7
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

- Interface methods must declare explicit return types.
- Objects and records can implement interfaces.
- Interface-typed locals, parameters, returns, fields, and arrays are supported.
- Interface method calls dispatch at runtime.
- Inline `implement Interface.method(...)` inherits the return type from the interface method.
- External `implement Interface for Type` maps interface signatures to concrete methods with `via`.

Common mistakes:

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
