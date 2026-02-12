using System;
using System.Collections.Generic;
using System.IO;

namespace ConsoleApp1;

internal static class TestHarness
{
    public static void RunAll()
    {
        string nl = Environment.NewLine;
        var tests = new List<(string Name, byte[] Program, string ExpectedOutput)>
        {
            ("arithmetic", BytecodeBuilder.New()
                .PushInt(2).PushInt(3).Add().PushInt(4).Mul().Print().Halt().ToArray(),
                "20" + nl),

            ("dup", BytecodeBuilder.New()
                .PushInt(7).Dup().Add().Print().Halt().ToArray(),
                "14" + nl),

            ("swap", BytecodeBuilder.New()
                .PushInt(1).PushInt(2).Swap().Sub().Print().Halt().ToArray(),
                "1" + nl),

            ("jump-if-zero skips", BytecodeBuilder.New()
                .PushInt(0).JumpIfZero("end")
                .PushInt(99).Print()
                .Label("end").Halt().ToArray(),
                ""),

            ("jump-if-not-zero prints", BytecodeBuilder.New()
                .PushInt(5).Dup().JumpIfNotZero("print")
                .Halt()
                .Label("print").Print().Halt().ToArray(),
                "5" + nl),

            ("unconditional jump", BytecodeBuilder.New()
                .Jump("start")
                .PushInt(999).Print() // should be skipped
                .Label("start")
                .PushInt(10).PushInt(2).Div().Print().Halt().ToArray(),
                "5" + nl),

            ("store/load", BytecodeBuilder.New()
                .PushInt(42).Store(0)
                .PushInt(1).Store(1)
                .Load(0).Load(1).Add().Print().Halt().ToArray(),
                "43" + nl),

            ("comparisons", BytecodeBuilder.New()
                .PushInt(3).PushInt(3).Eq().Print()
                .PushInt(2).PushInt(5).Lt().Print()
                .PushInt(7).PushInt(4).Gt().Print()
                .Halt().ToArray(),
                "1" + nl + "1" + nl + "1" + nl),

            ("branch on eq", BytecodeBuilder.New()
                .PushInt(10).PushInt(10).Eq().JumpIfZero("neq")
                .PushInt(1).Print().Jump("end")
                .Label("neq").PushInt(0).Print()
                .Label("end").Halt().ToArray(),
                "1" + nl),

            ("call/ret", BytecodeBuilder.New()
                .PushInt(2).PushInt(3).Call("add", 2, 2).Print().Halt()
                .Label("add")
                    .Load(0).Load(1).Add().Ret()
                .ToArray(),
                "5" + nl)
        };

        int failures = 0;
        foreach (var (name, program, expected) in tests)
        {
            using var writer = new StringWriter();
            var vm = new Vm(program, writer);
            try
            {
                vm.Run();
                var output = Normalize(writer.ToString());
                var expectNorm = Normalize(expected);
                if (!string.Equals(output, expectNorm, StringComparison.Ordinal))
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: expected '{Escape(expectNorm)}' got '{Escape(output)}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {name}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        if (failures > 0)
        {
            Console.WriteLine($"Tests failed: {failures}");
            Environment.Exit(1);
        }

        failures += RunCompilerIntegrationTests();
        failures += RunArithmeticFuzz();
        failures += RunBooleanFuzz();
        failures += RunStringConcatFuzz();
        failures += RunLoopFuzz();
        failures += RunPanicFuzz();

        if (failures > 0)
        {
            Console.WriteLine($"Tests failed: {failures}");
            Environment.Exit(1);
        }
    }

    private static string Escape(string value) =>
        value.Replace("\r", "\\r").Replace("\n", "\\n");

    private static int RunCompilerIntegrationTests()
    {
        int failures = 0;
        var cases = new List<(string Name, string Source, string Expected)>
        {
            ("print-string", @"print(""hi"");", "hi\n"),
            ("arith-assign", @"integer a = 2 + 3 * 4; print(a);", "14\n"),
            ("function-call",
@"function<integer> add(integer a, integer b) { return a + b; }
print(add(2, 3));",
             "5\n"),
            ("while-sum",
@"integer i = 0;
integer sum = 0;
while i < 4 then {
  sum = sum + i;
  i = i + 1;
}
print(sum);", "6\n"),
            ("foreach-count",
@"integer n = 3;
foreach i in n then print(i);",
 "0\n1\n2\n"),
            ("interp-string",
@"integer x = 3;
print(""x={x}"");", "x=3\n")
        };
        var arrayCases = new List<(string Name, string Source, string Expected)>
        {
            ("array-foreach", @"integer sum=0; foreach v in {1,2,3} then sum = sum + v; print(sum);", "6\n"),
            ("typed-array-literal", @"array<integer> items = {1,2,3}; integer s=0; foreach v in items then s = s + v; print(s);", "6\n"),
            ("new-array-sized", @"array<integer> items = new array<integer>(4); integer i = 0; while i < 4 then { i = i + 1; } print(i);", "4\n"),
            ("array-length-prop", @"array<integer> items = {1,2,3,4,5}; print(items.length);", "5\n"),
            ("array-index", @"array<integer> items = {10,20,30}; print(items[0]); print(items[2]);", "10\n30\n"),
            ("array-set", @"array<integer> items = {10,20,30}; items[1] = 99; print(items[0]); print(items[1]); print(items[2]);", "10\n99\n30\n"),
            ("optional-hasvalue", @"optional<integer> v; print(v.hasValue);", "0\n"),
            ("optional-or", @"optional<integer> v; print(v.or(42));", "42\n"),
            ("optional-some", @"optional<integer> v = 5; print(v.hasValue); print(v.value);", "1\n5\n")
        };
        var objectCases = new List<(string Name, string Source, string Expected)>
        {
            ("object-construct-and-field",
@"object Person {
  integer age;
  constructor(integer value) {
    this.age = value;
  }
}
Person p = new Person(42);
print(p.age);", "42\n"),
            ("object-field-set",
@"object Counter {
  integer count;
  constructor(integer start) {
    this.count = start;
  }
}
Counter c = new Counter(1);
c.count = c.count + 5;
print(c.count);", "6\n"),
            ("object-forward-field-ref",
@"object B {
  A a;
  constructor(A value) {
    this.a = value;
  }
}
object A {
  integer number;
  constructor(integer n) {
    this.number = n;
  }
}
A a = new A(9);
B b = new B(a);
print(b.a.number);", "9\n"),
            ("object-method-call",
@"object Person {
  integer age;
  constructor(integer years) {
    this.age = years;
  }
  function<integer> getAge() {
    return this.age;
  }
}
Person p = new Person(12);
print(p.getAge());", "12\n"),
            ("object-method-overload-arity",
@"object MathBox {
  integer baseValue;
  constructor(integer value) {
    this.baseValue = value;
  }
  function<integer> add(integer value) {
    return this.baseValue + value;
  }
  function<integer> add(integer left, integer right) {
    return left + right;
  }
}
MathBox box = new MathBox(4);
print(box.add(3));
print(box.add(2, 8));", "7\n10\n"),
            ("object-method-overload-types",
@"object Printer {
  integer baseValue;
  constructor(integer value) {
    this.baseValue = value;
  }
  function<integer> pick(integer value) {
    return value + this.baseValue;
  }
  function<integer> pick(boolean value) {
    return this.baseValue + 100;
  }
}
Printer p = new Printer(2);
print(p.pick(5));
print(p.pick(true));", "7\n102\n"),
            ("object-constructor-overload-types",
@"object Bag {
  integer count;
  constructor(integer v) {
    this.count = v;
  }
  constructor(boolean v) {
    this.count = 999;
  }
}
Bag a = new Bag(3);
Bag b = new Bag(true);
print(a.count);
print(b.count);", "3\n999\n"),
        };
        var interfaceCases = new List<(string Name, string Source, string Expected)>
        {
            ("interface-implement-ok",
@"interface ICounter {
  function<integer> read();
}
object Counter {
  integer x;
  constructor(integer v) {
    this.x = v;
  }
  function<integer> read() {
    return this.x;
  }
}
implement ICounter for Counter {
  read() via Counter.read;
}
ICounter c = new Counter(7);
print(c.read());", "7\n"),
            ("interface-runtime-dispatch",
@"interface IValue {
  function<integer> read();
}
object One {
  constructor() { }
  function<integer> read() {
    return 1;
  }
}
object Two {
  constructor() { }
  function<integer> read() {
    return 2;
  }
}
implement IValue for One {
  read() via One.read;
}
implement IValue for Two {
  read() via Two.read;
}
IValue v = new One();
print(v.read());
v = new Two();
print(v.read());", "1\n2\n"),
            ("interface-runtime-dispatch-via-alias-method",
@"interface IText {
  function<string> text(integer value);
}
object Printer {
  constructor() { }
  function<string> render(integer value) {
    return ""v="" + value;
  }
}
implement IText for Printer {
  text(integer value) via Printer.render;
}
IText t = new Printer();
print(t.text(3));", "v=3\n"),
            ("interface-field-dispatch",
@"interface IValue {
  function<integer> read();
}
object One {
  constructor() { }
  function<integer> read() {
    return 1;
  }
}
implement IValue for One {
  read() via One.read;
}
object Holder {
  IValue current;
  constructor(IValue initial) {
    this.current = initial;
  }
  function<integer> get() {
    return this.current.read();
  }
}
Holder h = new Holder(new One());
print(h.get());", "1\n"),
        };
        // Expected error cases
        var errorCases = new List<(string Name, string Source, string ExpectedType)>
        {
            ("panic-basic", @"panic(""boom"");", "UserError"),
        };
        var compileErrorCases = new List<(string Name, string Source, string ErrorContains)>
        {
            ("object-duplicate-name", @"object Person { integer age; constructor(integer v){ this.age = v; } } object Person { integer score; constructor(integer v){ this.score = v; } }", "already defined"),
            ("object-duplicate-field", @"object Person { integer age; integer age; constructor(integer v){ this.age = v; } }", "already defined"),
            ("object-unknown-field-type", @"object Person { UnknownType data; }", "Unknown type"),
            ("object-missing-constructor", @"object Person { integer age; }", "has no constructor"),
            ("object-missing-field-init", @"object Person { integer age; constructor() { } }", "does not definitely assign fields"),
            ("object-new-ctor-arity-mismatch", @"object Person { integer age; constructor(integer value) { this.age = value; } } Person p = new Person();", "No matching constructor overload"),
            ("object-method-missing-return", @"object A { integer x; constructor(integer v){ this.x = v; } function<integer> f() { integer y = 1; } }", "may not return"),
            ("object-method-undefined", @"object A { integer x; constructor(integer v){ this.x = v; } } A a = new A(1); print(a.nope());", "no matching method overload"),
            ("object-method-duplicate-arity", @"object A { integer x; constructor(integer v){ this.x = v; } function<integer> f(integer v) { return v; } function<integer> f(integer z) { return z; } }", "already defined"),
            ("object-method-no-compatible-overload",
@"object A {
  integer x;
  constructor(integer v){ this.x = v; }
  function<integer> f(real v) { return 1; }
  function<integer> f(integer z) { return 2; }
}
A a = new A(0);
print(a.f(true));", "no matching method overload"),
            ("interface-unknown", @"implement Missing for Thing { }", "Unknown interface"),
            ("interface-duplicate",
@"interface IThing {
  function<integer> id();
}
interface IThing {
  function<integer> id();
}", "already defined"),
            ("interface-map-missing-method",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<integer> value() {
    return 1;
  }
}
implement IThing for Thing {
  id() via Thing.id;
}", "has no method"),
            ("interface-map-return-mismatch",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<boolean> id() {
    return true;
  }
}
implement IThing for Thing {
  id() via Thing.id;
}", "return type does not satisfy interface"),
            ("interface-map-wrong-via-object",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<integer> id() {
    return 1;
  }
}
object OtherThing {
  constructor() { }
  function<integer> id() {
    return 2;
  }
}
implement IThing for Thing {
  id() via OtherThing.id;
}", "cannot map via"),
            ("interface-map-missing-required",
@"interface IThing {
  function<integer> id();
  function<integer> value();
}
object Thing {
  constructor() { }
  function<integer> id() {
    return 1;
  }
  function<integer> value() {
    return 2;
  }
}
implement IThing for Thing {
  id() via Thing.id;
}", "does not map interface method"),
            ("interface-assign-non-implementer",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<integer> id() {
    return 1;
  }
}
object Other {
  constructor() { }
}
implement IThing for Thing {
  id() via Thing.id;
}
IThing value = new Other();", "Initializer type mismatch"),
            ("interface-call-missing-member",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<integer> id() {
    return 1;
  }
}
implement IThing for Thing {
  id() via Thing.id;
}
IThing t = new Thing();
print(t.missing());", "has no matching method overload"),
            ("interface-field-assign-non-implementer",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  function<integer> id() {
    return 1;
  }
}
object Other {
  constructor() { }
}
implement IThing for Thing {
  id() via Thing.id;
}
object Holder {
  IThing current;
  constructor(IThing initial) {
    this.current = initial;
  }
}
Holder h = new Holder(new Thing());
h.current = new Other();", "Field assignment type mismatch"),
        };

        foreach (var (name, src, expected) in cases)
        {
            try
            {
                var output = Normalize(CompileAndRun(src));
                var expectNorm = Normalize(expected);
                if (output != expectNorm)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: expected '{Escape(expectNorm)}' got '{Escape(output)}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {name}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, src, expected) in arrayCases)
        {
            try
            {
                var output = Normalize(CompileAndRun(src));
                var expectNorm = Normalize(expected);
                if (output != expectNorm)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: expected '{Escape(expectNorm)}' got '{Escape(output)}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {name}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, src, expected) in objectCases)
        {
            try
            {
                var output = Normalize(CompileAndRun(src));
                var expectNorm = Normalize(expected);
                if (output != expectNorm)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: expected '{Escape(expectNorm)}' got '{Escape(output)}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {name}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, src, expected) in interfaceCases)
        {
            try
            {
                var output = Normalize(CompileAndRun(src));
                var expectNorm = Normalize(expected);
                if (output != expectNorm)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: expected '{Escape(expectNorm)}' got '{Escape(output)}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {name}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, src, expectedType) in errorCases)
        {
            try
            {
                CompileAndRunExpectError(src, expectedType);
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, src, errorContains) in compileErrorCases)
        {
            try
            {
                CompileExpectError(src, errorContains);
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        return failures;
    }

    private static int RunArithmeticFuzz(int iterations = 50)
    {
        int failures = 0;
        var rand = new Random(12345);
        for (int i = 0; i < iterations; i++)
        {
            string expr = BuildExpr(rand, depth: 0, out int expected);
            string src = $"print({expr});";
            try
            {
                var output = Normalize(CompileAndRun(src));
                if (!int.TryParse(output.Trim(), out int got) || got != expected)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] fuzz#{i}: {expr} expected {expected} got '{output.Trim()}'");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] fuzz#{i}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }
        if (failures == 0) Console.WriteLine("[PASS] arithmetic fuzz");
        return failures;
    }

    private static int RunBooleanFuzz(int iterations = 30)
    {
        int failures = 0;
        var rand = new Random(54321);
        for (int i = 0; i < iterations; i++)
        {
            string expr = BuildBoolExpr(rand, depth: 0, out bool expected);
            string src = $"print({expr});";
            try
            {
                var output = Normalize(CompileAndRun(src)).Trim();
                if (!int.TryParse(output, out int got) || (got != 0 && got != 1) || (got == 1) != expected)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] bool-fuzz#{i}: {expr} expected {(expected ? 1 : 0)} got '{output}'");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] bool-fuzz#{i}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }
        if (failures == 0) Console.WriteLine("[PASS] boolean fuzz");
        return failures;
    }

    private static int RunStringConcatFuzz(int iterations = 30)
    {
        int failures = 0;
        var rand = new Random(22222);
        for (int i = 0; i < iterations; i++)
        {
            string expr = BuildStringConcat(rand, parts: rand.Next(2, 5), out string expected);
            string src = $"print({expr});";
            try
            {
                var output = Normalize(CompileAndRun(src));
                if (output.TrimEnd('\n') != expected)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] string-fuzz#{i}: {expr} expected '{Escape(expected)}' got '{Escape(output.Trim())}'");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] string-fuzz#{i}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }
        if (failures == 0) Console.WriteLine("[PASS] string concat fuzz");
        return failures;
    }

    private static int RunLoopFuzz(int iterations = 20)
    {
        int failures = 0;
        var rand = new Random(33333);
        for (int i = 0; i < iterations; i++)
        {
            int n = rand.Next(0, 8);
            int expected = n * (n - 1) / 2;
            string src =
$@"integer i = 0;
integer sum = 0;
while i < {n} then {{
  sum = sum + i;
  i = i + 1;
}}
print(sum);";
            try
            {
                var output = Normalize(CompileAndRun(src)).Trim();
                if (!int.TryParse(output, out int got) || got != expected)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] loop-fuzz#{i}: n={n} expected {expected} got '{output}'");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] loop-fuzz#{i}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }
        if (failures == 0) Console.WriteLine("[PASS] loop fuzz");
        return failures;
    }

    private static int RunPanicFuzz(int iterations = 10)
    {
        int failures = 0;
        var rand = new Random(44444);
        for (int i = 0; i < iterations; i++)
        {
            string msg = $"boom{i}";
            string src = $"panic(\"{msg}\");";
            try
            {
                CompileAndRunExpectError(src, "UserError");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] panic-fuzz#{i}: {ex.Message}");
            }
        }
        if (failures == 0) Console.WriteLine("[PASS] panic fuzz");
        return failures;
    }

    private static string BuildExpr(Random rand, int depth, out int value)
    {
        if (depth > 3)
        {
            value = rand.Next(0, 6);
            return value.ToString();
        }
        // 50% chance leaf
        if (rand.NextDouble() < 0.5)
        {
            value = rand.Next(0, 6);
            return value.ToString();
        }

        string left = BuildExpr(rand, depth + 1, out int lv);
        string right = BuildExpr(rand, depth + 1, out int rv);
        char op = "+-*"[rand.Next(0, 3)];
        value = op switch
        {
            '+' => lv + rv,
            '-' => lv - rv,
            '*' => lv * rv,
            _ => lv
        };
        return $"({left} {op} {right})";
    }

    private static string BuildBoolExpr(Random rand, int depth, out bool value)
    {
        // base: comparison between two small ints
        if (depth > 2 || rand.NextDouble() < 0.4)
        {
            int a = rand.Next(0, 5);
            int b = rand.Next(0, 5);
            var ops = new[] { "<", ">", "==", "!=" };
            string op = ops[rand.Next(ops.Length)];
            value = op switch
            {
                "<" => a < b,
                ">" => a > b,
                "==" => a == b,
                "!=" => a != b,
                _ => false
            };
            return $"{a} {op} {b}";
        }

        string left = BuildBoolExpr(rand, depth + 1, out bool lv);
        string right = BuildBoolExpr(rand, depth + 1, out bool rv);
        var lop = rand.Next(0, 2) == 0 ? "and" : "or";
        value = lop == "and" ? (lv && rv) : (lv || rv);
        return $"({left} {lop} {right})";
    }

    private static string BuildStringConcat(Random rand, int parts, out string value)
    {
        var pieces = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts; i++)
        {
            bool asString = (i == 0) || rand.NextDouble() < 0.6; // ensure first part is string to avoid numeric-only addition
            if (asString)
            {
                char c = (char)('a' + rand.Next(0, 6));
                pieces.Add($"\"{c}\"");
                sb.Append(c);
            }
            else
            {
                int num = rand.Next(0, 5);
                pieces.Add(num.ToString());
                sb.Append(num.ToString());
            }
        }
        value = sb.ToString();
        return string.Join(" + ", pieces);
    }

    private static string CompileAndRun(string source)
    {
        var lexer = new Compiler.Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Compiler.Parser(tokens);
        var ast = parser.Parse();
        var typeChecker = new Compiler.TypeChecker();
        typeChecker.Check(ast);
        var generator = new Compiler.CodeGenerator();
        var bytes = generator.Generate(ast);
        using var sw = new StringWriter();
        var vm = new Vm(bytes, sw);
        vm.Run();
        return sw.ToString();
    }

    private static void CompileAndRunExpectError(string source, string expectedType)
    {
        try
        {
            CompileAndRun(source);
            throw new Exception("Expected runtime error was not thrown");
        }
        catch (VmRuntimeException vex)
        {
            if (vex.Error.Type != expectedType)
                throw new Exception($"Expected error type '{expectedType}' got '{vex.Error.Type}'");
        }
    }

    private static void CompileExpectError(string source, string expectedContains)
    {
        try
        {
            var lexer = new Compiler.Lexer(source);
            var tokens = lexer.ScanTokens();
            var parser = new Compiler.Parser(tokens);
            var ast = parser.Parse();
            var typeChecker = new Compiler.TypeChecker();
            typeChecker.Check(ast);
            throw new Exception("Expected compile error was not thrown");
        }
        catch (Compiler.CompilerException ex)
        {
            if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                throw new Exception($"Expected compile error containing '{expectedContains}', got '{ex.Message}'");
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
