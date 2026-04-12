# Code Language Reference

Status: developer reference for the implemented Code language surface.

This reference is for new developers learning Code from the current compiler, checked examples, and engine modules. It favors short examples and direct behavior notes over formal grammar. Draft or planned features live in [Planned and Draft-Only Features](planned-and-draft-only.md), not mixed into the implemented reference.

For deeper design notes, see [Code Language Specification](../code-language-spec.md), [Web App Runtime V1 Contract](../web-app-v1.md), and [Example Catalog](../example-catalog.md).

## Reading Order

1. [Syntax Basics](syntax-basics.md)
2. [Types and Values](types-and-values.md)
3. [Control Flow and Errors](control-flow-and-errors.md)
4. [Functions and Methods](functions-and-methods.md)
5. [Objects, Records, and Interfaces](objects-records-interfaces.md)
6. [Modules and Packages](modules-and-packages.md)
7. [Standard and Host Intrinsics](standard-and-host-intrinsics.md)
8. [Web Apps and Engine Modules](web-apps-and-engine.md)
9. [CLI and Tooling](cli-and-tooling.md)
10. [Planned and Draft-Only Features](planned-and-draft-only.md)

## Smallest Program

```code
print("hello, world");
```

Output:

```text
hello, world
```

## Run a Program

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- ConsoleApp1/examples/arithmetic.code
```

## Build a Web App

```powershell
dotnet run --project ConsoleApp1/ConsoleApp1.csproj -- --build-web ConsoleApp1/examples/shape_dodge.code
```

The web build emits a static site folder containing `index.html` and `app.bytecode`. If an `assets/` folder exists beside the entry file or package root, it is copied into the output.

## Reference Format

Most entries use this shape:

- Syntax: the form to write.
- Inputs: parameter or operand types.
- Returns: the result type or statement behavior.
- Example: short runnable Code.
- Output: printed result when useful.
- Common mistakes: compiler or runtime edges that are easy to hit.

## Implementation Truth

This reference treats these as the primary source of truth:

- `ConsoleApp1/Compiler/Lexer.cs`
- `ConsoleApp1/Compiler/Parser.cs`
- `ConsoleApp1/Compiler/TypeChecker.cs`
- `ConsoleApp1/Compiler/HostAbiCatalog.cs`
- `ConsoleApp1/examples/*.code`
- `lib/engine/*.code`

When an older draft document mentions a feature not accepted by the current parser or type checker, this reference lists it only in [Planned and Draft-Only Features](planned-and-draft-only.md).
