# Record Equality and Hashing

Records are value types. By default, every record field participates in equality
and hashing.

Hashing is used by `map` and `set` to find entries quickly. Equality is still
used after a hash match, so hash collisions do not make unequal values equal.

## Default Behavior

```code
record Stat {
  string name;
}

record Substance {
  string name;
  real densityGramsPerCm3;
  map<Stat, real> statsPerCm3;
}
```

`Substance` is hashable when all participating fields are hashable. Collections
are structural hash participants when their contained types are hashable.

Collection hashing rules:

- `array<T>` hashes values in index order.
- `queue<T>` hashes live values from front to back.
- `stack<T>` hashes live values from top to bottom.
- `set<T>` hashes elements without depending on insertion order.
- `map<K, V>` hashes key/value pairs without depending on insertion order.

Objects inside a hashed value use identity, not structural field equality.

## Selecting Key Fields

Use contextual `key` when only specific fields define the record identity:

```code
record Substance {
  key string name;
  key real densityGramsPerCm3;
  map<Stat, real> statsPerCm3;
}
```

If any field is marked `key`, only `key` fields participate in equality and
hashing. Key fields are constructor-only after initialization so map/set lookups
stay stable.

`key` is not a reserved word:

```code
integer key = 37;

record Lock {
  integer key;
}
```

## Ignoring Fields

Use contextual `ignore key` when most fields define identity but one field should
not:

```code
record Substance {
  string name;
  real densityGramsPerCm3;
  ignore key map<Stat, real> statsPerCm3;
}
```

A record may use `key` fields or `ignore key` fields, but not both.

## Hashability

Participating fields may contain:

- numeric, boolean, and string values
- enum values
- object references, hashed by identity
- hashable records
- `optional<T>` where `T` is hashable
- collections whose contained types are hashable

Interfaces and fallible values are not hashable in V1. Exclude them with
`ignore key`, or choose explicit `key` fields.

## Mutation Safety

When a value is inserted as a `map` key or `set` element, the runtime stores a
snapshot of the key/element for hashing and equality. Later mutation of the
original value does not corrupt the stored lookup key.

Common guidance:

- Use records for value keys.
- Use objects when identity/reference equality is intended.
- Use `key` for stable identity fields.
- Use `ignore key` for large, mutable, cached, or non-identifying payload fields.

## Direct-Wasm Status

The bytecode native VM, JavaScript reference web VM, and Rust/Wasm bytecode VM
support record and collection structural hashing. The experimental
`--web-backend direct-wasm` path currently rejects record/collection structural
equality and record/collection map/set keys rather than compiling identity-based
semantics by accident.
