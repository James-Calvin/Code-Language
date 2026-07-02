using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

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

            ("integer-division-bytecode", BytecodeBuilder.New()
                .PushInt(5).PushInt(2).IntDiv().Print()
                .PushInt(-5).PushInt(2).IntDiv().Print()
                .PushInt(5).PushInt(-2).IntDiv().Print()
                .PushInt(-5).PushInt(-2).IntDiv().Print()
                .Halt().ToArray(),
                "2" + nl + "-2" + nl + "-2" + nl + "2" + nl),

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
                "5" + nl),

            ("hostcall-print", BytecodeBuilder.New()
                .PushString("hi").HostCall("standard.input_output.print", 1).Pop().Halt().ToArray(),
                "hi" + nl),

            ("hostcall-print-legacy-alias", BytecodeBuilder.New()
                .PushString("hi").HostCall("std.io.print", 1).Pop().Halt().ToArray(),
                "hi" + nl),

            ("hostcall-engine-window-create", BytecodeBuilder.New()
                .PushString("demo").PushInt(640).PushInt(480)
                .HostCall("engine.window.create", 3).Print().Halt().ToArray(),
                "1" + nl),

            ("fallible-bytecode-success", BytecodeBuilder.New()
                .PushInt(7).FallibleSuccess().FallibleValue().Print().Halt().ToArray(),
                "7" + nl),

            ("fallible-bytecode-error-fields", BytecodeBuilder.New()
                .PushInt(2).PushString("bad").FallibleError()
                .Dup().FallibleIsError().Print()
                .Dup().FallibleErrorCode().Print()
                .FallibleErrorMessage().Print()
                .Halt().ToArray(),
                "1" + nl + "2" + nl + "bad" + nl),

            ("real-bytecode-and-casts", BytecodeBuilder.New()
                .PushReal(1.5).Print()
                .PushReal(3.8).CastInteger().Print()
                .PushReal(-3.8).CastInteger().Print()
                .PushInt(4).CastReal().Print()
                .Halt().ToArray(),
                "1.5" + nl + "3" + nl + "-3" + nl + "4" + nl),

            ("sized-numeric-bytecode", BytecodeBuilder.New()
                .PushWideInteger(4294967295L).CheckedSizedNumericCast(SizedNumericKind.Whole32).Print()
                .PushInt(255).CheckedSizedNumericCast(SizedNumericKind.Whole8).Print()
                .PushReal(3.8).CheckedSizedNumericCast(SizedNumericKind.Integer8).Print()
                .PushReal(1.25).CheckedSizedNumericCast(SizedNumericKind.Real32).Print()
                .Halt().ToArray(),
                "4294967295" + nl + "255" + nl + "3" + nl + "1.25" + nl),

            ("global-bytecode", BytecodeBuilder.New()
                .PushInt(41).StoreGlobal(0)
                .LoadGlobal(0).PushInt(1).Add().StoreGlobal(0)
                .LoadGlobal(0).Print()
                .Halt().ToArray(),
                "42" + nl)
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

        failures += RunBytecodeV10Tests();
        failures += RunDirectWasmEncodingTests();

        if (failures > 0)
        {
            Console.WriteLine($"Tests failed: {failures}");
            Environment.Exit(1);
        }

        failures += RunCompilerIntegrationTests();
        failures += RunWebBuildTests();
        failures += RunExampleCatalogTests();
        failures += RunWebRuntimeParityTests();
        failures += RunHostAbiSurfaceTests();
        failures += RunTargetParityTests();
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

    private static int RunBytecodeV10Tests()
    {
        int failures = 0;
        byte[] valid = BytecodeBuilder.New().PushString("pooled").Print().Halt().ToArray();

        failures += ExpectBytecodeFailure("bytecode-v9-rejected", Mutate(valid, bytes => bytes[4] = 9), "Unsupported bytecode version 9");
        failures += ExpectBytecodeFailure("bytecode-v10-truncated-metadata", valid[..^1], "metadata size is invalid");
        failures += ExpectBytecodeFailure("bytecode-v10-missing-metadata-magic", Mutate(valid, bytes =>
        {
            var header = BytecodeFormat.ReadHeader(bytes);
            bytes[BytecodeFormat.GetMetadataOffset(header)] = (byte)'X';
        }), "metadata magic is missing");
        failures += ExpectBytecodeFailure("bytecode-v10-invalid-string-id", Mutate(valid, bytes =>
        {
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, BytecodeFormat.HeaderSize + 1);
        }), "string index");

        try
        {
            string text = Disassembler.Disassemble(valid);
            if (!text.Contains("#0 \"pooled\"", StringComparison.Ordinal)) throw new Exception("pooled string was not resolved");
            Console.WriteLine("[PASS] bytecode-v10-disassembler-metadata");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] bytecode-v10-disassembler-metadata: {ex.Message}");
        }
        return failures;
    }

    private static byte[] Mutate(byte[] source, Action<byte[]> mutation)
    {
        byte[] copy = (byte[])source.Clone();
        mutation(copy);
        return copy;
    }

    private static int RunDirectWasmEncodingTests()
    {
        int failures = 0;
        void Check(string name, Action test)
        {
            try { test(); Console.WriteLine($"[PASS] {name}"); }
            catch (Exception ex) { failures++; Console.WriteLine($"[FAIL] {name}: {ex.Message}"); }
        }

        Check("direct-wasm-leb128-unsigned", () =>
        {
            var bytes = new List<byte>();
            Compiler.DirectWasmEncoding.WriteU32(bytes, 624485);
            if (!bytes.SequenceEqual(new byte[] { 0xe5, 0x8e, 0x26 })) throw new Exception("incorrect unsigned LEB128 encoding");
        });
        Check("direct-wasm-leb128-signed", () =>
        {
            var bytes = new List<byte>();
            Compiler.DirectWasmEncoding.WriteS32(bytes, -123456);
            if (!bytes.SequenceEqual(new byte[] { 0xc0, 0xbb, 0x78 })) throw new Exception("incorrect signed LEB128 encoding");
        });
        Check("direct-wasm-module-sections", () =>
        {
            var module = new Compiler.DirectWasmModuleBuilder();
            int function = module.ReserveFunction("answer", [], [Compiler.DirectWasmValueType.I64]);
            module.GetFunctionBody(function).I64Const(42);
            module.ExportFunction("answer", function);
            byte[] bytes = module.Build();
            if (!bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0, 0x61, 0x73, 0x6d, 1, 0, 0, 0 }))
                throw new Exception("Wasm magic/version header is invalid");
            if (!bytes.Contains((byte)10)) throw new Exception("code section is missing");
        });
        Check("direct-wasm-typed-program", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "code-direct-wasm-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "main.code");
                File.WriteAllText(source, "integer value = 0; foreach index in 10 then value += index; print(value == 45);");
                var result = Compiler.ModuleCompiler.CompileFromFileWithMetadata(source, new Compiler.ModuleCompileOptions
                {
                    Target = Compiler.CompileTarget.VmWeb,
                    EmitDirectWasm = true
                });
                if (result.DirectWasm is null || result.DirectWasm.Module.Length < 32) throw new Exception("direct module was not emitted");
                if (result.DirectWasm.FunctionCount < 2) throw new Exception("typed functions were not emitted");
            }
            finally { try { Directory.Delete(root, recursive: true); } catch { } }
        });
        Check("direct-wasm-disable-gc-option", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "code-direct-wasm-gc-option-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "main.code");
                File.WriteAllText(source, "function start() {} function update() {} function draw() { Draw.clearScreen(Colors.rgb(0, 0, 0)); }");
                var result = Compiler.ModuleCompiler.CompileFromFileWithMetadata(source, new Compiler.ModuleCompileOptions
                {
                    Target = Compiler.CompileTarget.VmWeb,
                    EnableGraphicalAppProfile = true,
                    EnableImpliedEngineImports = true,
                    EmitDirectWasm = true,
                    DisableDirectWasmGarbageCollection = true
                });
                if (result.DirectWasm is null) throw new Exception("direct module was not emitted");
                if (!result.DirectWasm.GarbageCollectionDisabled) throw new Exception("direct module did not retain disabled-GC metadata");
            }
            finally { try { Directory.Delete(root, recursive: true); } catch { } }
        });
        Check("direct-wasm-disable-gc-cli-validation", () =>
        {
            string? error = Program.ValidateDirectWasmGarbageCollectionFlag(disableGarbageCollection: true, directWasmBackend: false);
            if (string.IsNullOrWhiteSpace(error) || !error.Contains("--web-backend direct-wasm", StringComparison.Ordinal))
                throw new Exception("missing validation error for default backend");
            if (Program.ValidateDirectWasmGarbageCollectionFlag(disableGarbageCollection: true, directWasmBackend: true) is not null)
                throw new Exception("direct-wasm backend should accept disabled GC");
        });
        return failures;
    }

    private static int ExpectBytecodeFailure(string name, byte[] bytes, string expected)
    {
        try
        {
            using var writer = new StringWriter();
            new Vm(bytes, writer).Run();
            Console.WriteLine($"[FAIL] {name}: expected failure");
            return 1;
        }
        catch (Exception ex) when (ex.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[PASS] {name}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: expected '{expected}', got '{ex.Message}'");
            return 1;
        }
    }

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
            ,
            ("interp-expression-only-is-string",
@"integer x = 3;
print(""{x}"" == ""3"");", "1\n")
            ,
            ("interp-adjacent-expressions-concatenate",
@"integer x = 3;
integer y = 4;
print(""{x}{y}"");", "34\n")
            ,
            ("interp-escaped-braces",
@"integer x = 7;
print(""literal \{braces\} and x={x}"");", "literal {braces} and x=7\n")
            ,
            ("integer-base-prefixes",
@"print(0b1010);
print(0o17);
print(0x1f);", "10\n15\n31\n")
            ,
            ("real-literal-dot-forms",
@"print(1.5);
print(1.);
print(.5);", "1.5\n1\n0.5\n")
            ,
            ("real-literal-arithmetic-and-widening",
@"real value = 1.5 + 2;
print(value);
print(3 as real);
print(1 + 2 as real);", "3.5\n3\n3\n")
            ,
            ("numeric-casts",
@"print(3.8 as integer);
print(-3.8 as integer);
print(3 as whole);
print((3 as whole) as real);", "3\n-3\n3\n3\n")
            ,
            ("sized-numerics",
@"byte channel = 255;
whole8 same = channel;
whole16 wider_whole = channel;
integer16 wider_signed = channel;
integer8 small = -128;
integer16 wider_signed_from_small = small;
integer32 signed_min = -2147483648;
integer32 signed_max = 2147483647;
whole32 whole_max = 4294967295;
whole32 hex_max = 0xffffffff;
real32 rough = 1.25 as real32;
real64 exact = rough;
real from_integer32 = signed_max;
byte value = 250;
value += 5;
whole16 cast_wide = 300 as whole16;
integer8 truncated = 3.8 as integer8;
array<whole8> bytes = new array<whole8>(0);
bytes.append(channel);
print(same);
print(wider_whole);
print(wider_signed);
print(wider_signed_from_small);
print(signed_min);
print(signed_max);
print(whole_max);
print(hex_max);
print(exact);
print(from_integer32);
print(value);
print(cast_wide);
print(truncated);
print(bytes.length);
print(bytes[0]);", "255\n255\n255\n-128\n-2147483648\n2147483647\n4294967295\n4294967295\n1.25\n2147483647\n255\n300\n3\n1\n255\n")
            ,
            ("integer-division",
@"print(5 / 2);
print(-5 / 2);
print(5 / -2);
print(-5 / -2);
print(5. / 2);
print(5 / 2.);", "2\n-2\n-2\n2\n2.5\n2.5\n")
            ,
            ("enum-casts",
@"enum Direction {
  Left = 1;
  Right = 2;
}
Direction direction = 2 as Direction;
print(direction == Direction.Right);
print(Direction.Left as integer);
integer dynamic = 1 + 1;
Direction other = dynamic as Direction;
print(other == Direction.Right);", "1\n1\n1\n")
            ,
            ("while-break-continue",
@"integer i = 0;
integer sum = 0;
while i < 6 then {
  i += 1;
  if i == 2 then continue;
  if i == 5 then break;
  sum += i;
}
print(sum);", "8\n")
            ,
            ("for-break-continue",
@"integer sum = 0;
for integer i = 0; i < 6; i += 1 then {
  if i == 1 then continue;
  if i == 4 then break;
  sum += i;
}
print(sum);", "5\n")
            ,
            ("foreach-break-continue",
@"integer sum = 0;
foreach value in {1, 2, 3, 4, 5} then {
  if value == 2 then continue;
  if value == 5 then break;
  sum += value;
}
print(sum);", "8\n")
            ,
            ("switch-basic",
@"integer value = 2;
switch value then {
  case 1 then print(""one"");
  case 2 then print(""two"");
  default then print(""other"");
}", "two\n")
            ,
            ("switch-evaluates-value-once",
@"object Counter {
  integer calls;
  constructor() {
    calls = 0;
  }
  function<integer> nextValue() {
    calls += 1;
    return 2;
  }
}
Counter counter = new Counter();
switch counter.nextValue() then {
  case 1 then print(""one"");
  case 2 then print(""two"");
  default then print(""other"");
}
print(counter.calls);", "two\n1\n")
            ,
            ("switch-enum",
@"enum Direction {
  Left;
  Right;
}
Direction direction = Direction.Right;
switch direction then {
  case Direction.Left then print(""left"");
  case Direction.Right then print(""right"");
  default then print(""other"");
}", "right\n")
            ,
            ("enum-basic",
@"enum Difficulty {
  Easy;
  Normal = 5;
  Hard;
}
Difficulty difficulty = Difficulty.Easy;
print(difficulty == Difficulty.Easy);
difficulty = Difficulty.Hard;
print(difficulty == Difficulty.Hard);
print(Difficulty.Normal);
print(Difficulty.Hard);", "1\n1\n5\n6\n")
            ,
            ("enum-array-and-parameter",
@"enum Direction {
  Left;
  Right;
}
function<Direction> choose(boolean goRight) {
  if goRight then return Direction.Right;
  return Direction.Left;
}
array<Direction> directions = {Direction.Left, choose(true)};
print(directions[0] == Direction.Left);
print(directions[1] == Direction.Right);", "1\n1\n")
            ,
            ("record-copy-assignment",
@"record Stats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
}
Stats original = new Stats(3);
Stats copied = original;
original.strength = 9;
print(original.strength);
print(copied.strength);", "9\n3\n")
            ,
            ("record-pass-and-return-by-value",
@"record Stats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
}
function<Stats> boost(Stats stats) {
  stats.strength += 2;
  return stats;
}
Stats original = new Stats(4);
Stats result = boost(original);
print(original.strength);
print(result.strength);", "4\n6\n")
            ,
            ("record-nested-and-optional-copy",
@"record Stats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
}
record Profile {
  Stats stats;
  optional<Stats> backup;
  constructor(integer strength) {
    stats = new Stats(strength);
    backup = stats;
  }
}
Profile first = new Profile(5);
Profile second = first;
first.stats.strength = 9;
Stats backup = second.backup.value;
print(second.stats.strength);
print(backup.strength);", "5\n5\n")
            ,
            ("record-method-value-receiver",
@"record Stats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
  function<Stats> boosted(integer amount) {
    strength += amount;
    return this;
  }
  function<integer> read() {
    return strength;
  }
}
Stats base = new Stats(3);
Stats boosted = base.boosted(2);
array<Stats> items = new array<Stats>(0);
items.append(base);
Stats from_item = items[0].boosted(4);
print(base.strength);
print(boosted.strength);
print(items[0].strength);
print(from_item.read());", "3\n5\n3\n7\n")
            ,
            ("record-interface-inline-and-external",
@"interface Reader {
  function<integer> read();
}
record InlineStats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
  implement Reader.read() {
    return strength;
  }
}
record ExternalStats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
  function<integer> read() {
    return strength;
  }
}
implement Reader for ExternalStats {
  read() via ExternalStats.read;
}
function<integer> sample(Reader reader) {
  return reader.read();
}
function<Reader> expose_inline(InlineStats stats) {
  return stats;
}
InlineStats inline_stats = new InlineStats(4);
ExternalStats external_stats = new ExternalStats(6);
Reader first = expose_inline(inline_stats);
array<Reader> readers = new array<Reader>(0);
readers.append(external_stats);
print(inline_stats.strength);
print(first.read());
print(sample(external_stats));
print(readers[0].read());", "4\n4\n6\n6\n")
            ,
            ("record-hashable-equality-and-collections",
@"record Point {
  integer x;
  integer y;
  constructor(integer x, integer y) {
    this.x = x;
    this.y = y;
  }
}
Point left = new Point(1, 2);
Point right = new Point(1, 2);
optional<Point> left_optional = left;
optional<Point> right_optional = right;
set<Point> points = new set<Point>();
points.add(left);
map<Point, integer> scores = new map<Point, integer>();
scores[left] = 7;
left.x = 9;
print(left == right);
print(left_optional == right_optional);
print(points.contains(right));
print(scores[right]);", "0\n1\n1\n7\n")
            ,
            ("modulo-op", @"integer value = 8 % 3; print(value);", "2\n"),
            ("function-void",
@"function printHello() {
  print(""hello"");
}
function<void> printWorld() {
  print(""world"");
}
printHello();
printWorld();", "hello\nworld\n"),
            ("enhanced-assignments",
@"integer value = 0;
value += 5;
print(value);
value *= 3;
print(value);
value /= 2;
print(value);
value--;
print(value);
value %= 4;
print(value);", "5\n15\n7\n6\n2\n"),
            ("enhanced-assignments-field-and-array-targets",
@"object Box {
  integer count;
  constructor() {
    this.count = 1;
  }
}
object Holder {
  integer calls;
  Box box;
  array<integer> items;
  constructor() {
    this.calls = 0;
    this.box = new Box();
    this.items = {3, 10};
  }
  function<Box> getBox() {
    this.calls += 1;
    return this.box;
  }
  function<array<integer>> getItems() {
    this.calls += 1;
    return this.items;
  }
  function<integer> nextIndex() {
    this.calls += 1;
    return 0;
  }
}
Holder holder = new Holder();
holder.getBox().count += 4;
holder.getItems()[holder.nextIndex()] *= 2;
print(holder.box.count);
print(holder.items[0]);
print(holder.calls);", "5\n6\n3\n"),
            ("constant-ok", @"constant real circleValue = 3; print(circleValue);", "3\n")
            ,
            ("same-module-globals-and-built-in-constants",
@"constant real scale = tau;
integer counter = 1;

object Thing {
  real angle = scale;

  constructor() {
  }

  function update() {
    counter += 1;
  }

  function printValues() {
    print(angle);
    print(pi);
  }
}

function bump() {
  counter++;
}

Thing thing = new Thing();
thing.update();
bump();
thing.printValues();
print(counter);", "6.283185307179586\n3.141592653589793\n3\n")
            ,
            ("global-shadowing-rules",
@"integer speed = 2;

object Player {
  integer speed;

  constructor() {
    speed = 5;
  }

  function printSpeed(integer speed) {
    print(speed);
    integer pi = 7;
    print(pi);
    print(this.speed);
  }
}

Player player = new Player();
player.printSpeed(11);
print(speed);
print(pi);", "11\n7\n5\n2\n3.141592653589793\n")
            ,
            ("time-intrinsics",
@"print(unixMilliseconds() > 0);
print(unixMicroseconds() > 0);
print(monotonicNanoseconds() >= 0);
print(monotonicTicks() > 0);
print(monotonicTicksPerSecond() > 0);",
             "1\n1\n1\n1\n1\n")
            ,
            ("math-random-intrinsics",
@"print(minimum(4, 9));
print(maximum(4, 9));
print(absolute(-3));
print(sign(-3));
print(sign(0));
print(sign(3));
print(lerp(10, 20, 1. / 4));
print(sine(0));
print(cosine(0));
real value = random();
print(value >= 0 and value < 1);",
             "4\n9\n3\n-1\n0\n1\n12.5\n0\n1\n1\n")
            ,
            ("fallible-success-and-error-handling",
@"enum ParseError {
  Empty;
  Invalid;
}
function<fallible<integer, ParseError>> parse_number(string text) {
  if text == """" then return error(ParseError.Empty, ""text was empty"");
  if text == ""one"" then return 1;
  return error(ParseError.Invalid, ""not a number"");
}
integer first = parse_number(""one"") on error {
  print(""should not run"");
  yield 0;
};
integer second = parse_number("""") on error {
  print(error.message);
  switch error.code then {
    case ParseError.Empty then yield 10;
    case ParseError.Invalid then yield 20;
    default then yield 0;
  }
};
print(first);
print(second);", "text was empty\n1\n10\n")
            ,
            ("fallible-integer-error-code",
@"function<fallible<integer, integer>> load_count(boolean ok) {
  if ok then return 4;
  return error(404);
}
integer value = load_count(false) on error {
  print(error.code);
  print(error.message == """");
  yield 9;
};
print(value);", "404\n1\n9\n")
            ,
            ("fallible-shorthand-message-only-error",
@"function<fallible<integer>> load_count(boolean ok) {
  if ok then return 4;
  return error(""missing count"");
}
integer value = load_count(false) on error {
  print(error.code);
  print(error.message);
  yield 9;
};
print(value);", "0\nmissing count\n9\n")
            ,
            ("fallible-shorthand-and-explicit-integer-interoperate",
@"function<fallible<integer>> load_short() {
  return error(5, ""short"");
}
function<fallible<integer, integer>> load_full() {
  return load_short();
}
integer value = load_full() on error {
  print(error.code);
  print(error.message);
  yield 3;
};
print(value);", "5\nshort\n3\n")
            ,
            ("fallible-return-existing-fallible",
@"enum LoadError {
  Missing;
}
function<fallible<integer, LoadError>> inner() {
  return error(LoadError.Missing, ""missing"");
}
function<fallible<integer, LoadError>> outer() {
  return inner();
}
integer value = outer() on error {
  print(error.message);
  yield 3;
};
print(value);", "missing\n3\n")
            ,
            ("fallible-handler-return-exits-enclosing-function",
@"enum LoadError {
  Missing;
  Invalid;
}
function<fallible<integer, LoadError>> inner() {
  return error(LoadError.Missing, ""missing"");
}
function<fallible<integer, LoadError>> outer() {
  integer value = inner() on error {
    return error(LoadError.Invalid, ""wrapped"");
  };
  return value;
}
integer value = outer() on error {
  print(error.message);
  yield 8;
};
print(value);", "wrapped\n8\n")
            ,
            ("draw-rectangle-canonical",
@"drawRectangle(0, 0, 8, 8, 1, 1, 1, 1);
print(1);", "1\n")
        };
        var arrayCases = new List<(string Name, string Source, string Expected)>
        {
            ("array-foreach", @"integer sum=0; foreach v in {1,2,3} then sum = sum + v; print(sum);", "6\n"),
            ("typed-array-literal", @"array<integer> items = {1,2,3}; integer s=0; foreach v in items then s = s + v; print(s);", "6\n"),
            ("new-array-sized", @"array<integer> items = new array<integer>(4); integer i = 0; while i < 4 then { i = i + 1; } print(i);", "4\n"),
            ("array-length-prop", @"array<integer> items = {1,2,3,4,5}; print(items.length);", "5\n"),
            ("array-index", @"array<integer> items = {10,20,30}; print(items[0]); print(items[2]);", "10\n30\n"),
            ("array-set", @"array<integer> items = {10,20,30}; items[1] = 99; print(items[0]); print(items[1]); print(items[2]);", "10\n99\n30\n"),
            ("array-compound-assignment", @"array<integer> items = {10,20,30}; integer index = 1; items[index] += 5; items[index]--; print(items[index]);", "24\n"),
            ("array-append-remove", @"array<integer> items = new array<integer>(0); items.append(10); items.append(20); items.removeAt(0); print(items.length); print(items[0]);", "1\n20\n"),
            ("array-record-copy-boundaries",
@"record Stats {
  integer strength;
  constructor(integer strength) {
    this.strength = strength;
  }
}
array<Stats> items = new array<Stats>(0);
Stats current = new Stats(1);
items.append(current);
current.strength = 7;
print(items[0].strength);
foreach item in items then {
  item.strength = 9;
}
print(items[0].strength);", "1\n1\n"),
            ("built-in-collections",
@"map<string, integer> scores = new map<string, integer>();
scores[""coins""] = 10;
scores[""coins""] += 5;
print(scores.length);
print(scores.contains(""coins""));
print(scores[""coins""]);
scores.remove(""coins"");
print(scores.contains(""coins""));
print(scores.length);
set<string> tags = new set<string>();
tags.add(""web"");
tags.add(""web"");
tags.add(""game"");
print(tags.length);
print(tags.contains(""web""));
tags.remove(""web"");
print(tags.contains(""web""));
queue<integer> turns = new queue<integer>();
turns.enqueue(3);
turns.enqueue(5);
print(turns.length);
print(turns.peek());
print(turns.dequeue());
print(turns.length);
stack<string> history = new stack<string>();
history.push(""start"");
history.push(""play"");
print(history.length);
print(history.peek());
print(history.pop());
print(history.length);",
                "1\n1\n15\n0\n0\n2\n1\n0\n2\n3\n3\n1\n2\nplay\nplay\n1\n"),
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
            ("object-field-defaults-no-constructor",
@"object Counter {
  integer count = 5;
  string label = ""ready"";

  function<integer> read() {
    return count;
  }
}
Counter c = new Counter();
print(c.count);
print(c.label);
print(c.read());", "5\nready\n5\n"),
            ("object-field-defaults-before-constructor",
@"object Meter {
  integer amount = 7;
  integer doubled;

  constructor() {
    doubled = amount * 2;
    amount += 1;
  }
}
Meter meter = new Meter();
print(meter.amount);
print(meter.doubled);", "8\n14\n"),
            ("record-field-defaults",
@"record Point {
  integer x = 2;
  integer y = 3;

  function<Point> moved(integer amount) {
    x += amount;
    y += amount;
    return this;
  }
}
Point start = new Point();
Point moved = start.moved(5);
print(start.x);
print(moved.x);
print(moved.y);", "2\n7\n8\n"),
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
            ("object-field-compound-assignment",
@"object Counter {
  integer count;
  constructor(integer start) {
    this.count = start;
  }
  function bump(integer amount) {
    this.count += amount;
    this.count--;
  }
}
Counter c = new Counter(1);
c.bump(5);
print(c.count);", "5\n"),
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
            ("object-field-interp",
@"object Stats {
  integer strength;
  constructor() {
    this.strength = 0;
  }
}
Stats player = new Stats();
print(""Strength: {player.strength}"");", "Strength: 0\n"),
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
            ("object-method-implicit-void",
@"object Logger {
  constructor() { }
  function ping() {
    print(""ok"");
  }
}
Logger logger = new Logger();
logger.ping();", "ok\n"),
            ("object-constructor-switch-definite-init",
@"enum Mode {
  A;
  B;
}
object Config {
  integer amount;
  constructor(Mode mode) {
    switch mode then {
      case Mode.A then amount = 1;
      case Mode.B then amount = 2;
      default then amount = 3;
    }
  }
}
Config config = new Config(Mode.B);
print(config.amount);", "2\n"),
            ("object-implicit-this-field-read-write",
@"object Counter {
  integer count;
  constructor() {
    count = 1;
  }
  function bump() {
    count += 4;
    count--;
  }
  function<integer> read() {
    return count;
  }
}
Counter c = new Counter();
c.bump();
print(c.read());", "4\n"),
            ("object-implicit-this-shadow-local-and-explicit-this",
@"object Counter {
  integer count;
  constructor() {
    count = 9;
  }
  function<integer> read() {
    integer count = 3;
    print(count);
    return this.count;
  }
}
Counter c = new Counter();
print(c.read());", "3\n9\n"),
            ("object-implicit-this-shadow-parameter",
@"object Counter {
  integer count;
  constructor() {
    count = 9;
  }
  function<integer> choose(integer count) {
    return count;
  }
}
Counter c = new Counter();
print(c.choose(4));
print(c.count);", "4\n9\n"),
            ("object-implicit-this-method-call",
@"object Greeter {
  integer calls;
  constructor() {
    calls = 0;
    ping();
  }
  function ping() {
    calls += 1;
  }
  function<integer> read() {
    ping();
    return calls;
  }
}
Greeter g = new Greeter();
print(g.read());", "2\n"),
            ("object-implicit-this-field-object-method-target",
@"object Child {
  integer data;
  constructor(integer v) {
    data = v;
  }
  function<integer> read() {
    return data;
  }
}
object Parent {
  Child child;
  constructor() {
    child = new Child(7);
  }
  function<integer> read() {
    return child.read();
  }
}
Parent p = new Parent();
print(p.read());", "7\n"),
            ("object-implicit-this-method-precedence",
@"function<integer> move() {
  return 100;
}
object Walker {
  integer steps;
  constructor() {
    steps = 0;
  }
  function move() {
    steps += 1;
  }
  function<integer> read() {
    move();
    return steps;
  }
}
Walker w = new Walker();
print(w.read());", "1\n"),
            ("object-member-private-access-inside-type",
@"object Box {
  private integer amount;
  public constructor(integer start) {
    amount = start;
  }
  private function<integer> hidden() {
    return amount;
  }
  public function<integer> add(Box other) {
    return hidden() + other.amount;
  }
}
Box left = new Box(3);
Box right = new Box(4);
print(left.add(right));", "7\n"),
            ("object-member-private-constructor-inside-type",
@"object Secret {
  private integer amount;
  public constructor() {
    amount = 1;
  }
  private constructor(integer nextValue) {
    amount = nextValue;
  }
  public function<Secret> next() {
    return new Secret(amount + 1);
  }
  public function<integer> read() {
    return amount;
  }
}
Secret first = new Secret();
Secret second = first.next();
print(second.read());", "2\n"),
            ("record-member-private-field-and-method",
@"record Point {
  private integer x;
  public constructor(integer value) {
    x = value;
  }
  private function<integer> doubled() {
    return x * 2;
  }
  public function<integer> read() {
    return doubled();
  }
}
Point point = new Point(5);
print(point.read());", "10\n"),
            ("scene-intrinsics-native-stubs",
@"object MainScene {
  constructor() { }
  function start() {
    print(""start"");
  }
  function update() {
    print(inputKeyDown(37));
    print(inputPointerWorldX());
    print(inputPointerWorldY());
    print(inputPointerScreenX());
    print(inputPointerScreenY());
    print(inputPointerIsDown());
    print(inputPointerWasPressed());
    print(inputPointerWasReleased());
  }
  function draw() {
    clear(0, 0, 0, 1);
    drawRectangle(cameraViewLeft(), cameraViewTop(), 30, 40, 1, 1, 1, 1);
    drawRectangleOutline(cameraViewLeft() + 4, cameraViewTop() + 4, 20, 30, 2, 1, 1, 1, 1);
    drawCircle(cameraSafeLeft() + 20, cameraSafeTop() + 20, 10, 1, 1, 1, 1);
    drawCircleOutline(cameraSafeLeft() + 20, cameraSafeTop() + 20, 16, 2, 1, 1, 1, 1);
    drawPolygon({0, 0, 10, 0, 5, 10}, 1, 1, 1, 1);
    drawPolygonOutline({0, 0, 10, 0, 5, 10}, 2, 1, 1, 1, 1);
    drawLine(cameraSafeLeft(), cameraSafeTop(), cameraSafeRight(), cameraSafeBottom(), 1, 1, 1, 1);
    drawImage(""assets/example.svg"", 0, 0, 16, 16, 1);
    drawSprite(""assets/example.svg"", 0, 0, 8, 8, 16, 16, 8, 8, 1);
    print(cameraViewLeft());
    print(cameraViewTop());
    print(cameraViewWidth());
    print(cameraViewHeight());
    print(cameraViewRight());
    print(cameraViewBottom());
    print(cameraSafeWidth());
    print(cameraSafeHeight());
  }
  function drawHud() {
    drawRectangle(screenWidth() - 10, screenHeight() - 10, 8, 8, 1, 1, 1, 1);
    drawText(""hud"", screenWidth() - 12, 12, 12, ""right"", ""top"", 1, 1, 1, 1);
    print(screenWidth());
    print(screenHeight());
  }
}
MainScene scene = new MainScene();
scene.start();
scene.update();
scene.draw();
scene.drawHud();", "start\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n640\n360\n640\n360\n640\n360\n640\n360\n"),
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
            ("interface-array-container-dispatch",
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
array<IValue> items = new array<IValue>(0);
items.append(new One());
items.append(new Two());
print(items[0].read());
foreach item in items then print(item.read());", "1\n1\n2\n"),
            ("interface-inline-implement-method",
@"interface IValue {
  function<integer> read();
}
object Counter {
  integer count;

  constructor(integer initial) {
    this.count = initial;
  }

  implement IValue.read() {
    return count;
  }
}
IValue item = new Counter(7);
print(item.read());", "7\n"),
        };
        var moduleCases = new List<(string Name, IReadOnlyDictionary<string, string> Files, string Entry, string Expected)>
        {
            (
                "module-import-export-function",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import add from \"math.code\";\nprint(add(2, 3));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "5\n"
            ),
            (
                "module-import-export-alias",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import add as plus from \"math.code\";\nprint(plus(4, 5));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "9\n"
            ),
            (
                "module-public-visibility-import",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import add from \"math.code\";\nprint(add(3, 4));",
                    ["math.code"] = "public function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "7\n"
            ),
            (
                "module-private-visibility-local-only",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"private function<integer> hidden() { return 7; }
function<integer> read() { return hidden(); }
print(read());",
                },
                "main.code",
                "7\n"
            ),
            (
                "module-package-visibility-same-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package demo.pkg;
import helper from ""helper.code"";
print(helper(4));",
                    ["helper.code"] =
@"package demo.pkg;
package function<integer> helper(integer value) { return value + 2; }",
                },
                "main.code",
                "6\n"
            ),
            (
                "module-member-package-visibility-same-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package demo.pkg;
import Box from ""box.code"";
Box box = new Box(3);
box.add_bonus();
print(box.read());
print(box.bonus);",
                    ["box.code"] =
@"package demo.pkg;
public object Box {
  private integer amount;
  package integer bonus;

  public constructor(integer start) {
    amount = start;
    bonus = 5;
  }

  private function clamp() {
    if amount < 0 then {
      amount = 0;
    }
  }

  package function add_bonus() {
    amount += bonus;
    clamp();
  }

  public function<integer> read() {
    return amount;
  }
}",
                },
                "main.code",
                "8\n5\n"
            ),
            (
                "module-object-uses-own-module-global",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Counter from ""counter.code"";
Counter counter = new Counter();
counter.step();
counter.step();
print(counter.read());",
                    ["counter.code"] =
@"integer shared = 10;

export object Counter {
  constructor() {
  }

  function step() {
    shared += 1;
  }

  function<integer> read() {
    return shared;
  }
}",
                },
                "main.code",
                "12\n"
            ),
            (
                "module-grouped-imports",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import { add, sub as minus } from \"math.code\";\nprint(add(7, 2));\nprint(minus(7, 2));",
                    ["math.code"] =
@"export function<integer> add(integer a, integer b) { return a + b; }
export function<integer> sub(integer a, integer b) { return a - b; }",
                },
                "main.code",
                "9\n5\n"
            ),
            (
                "module-namespace-import-functions",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import everything as Math from ""math.code"";
print(Math.add(7, 2));
print(Math.sub(7, 2));",
                    ["math.code"] =
@"export function<integer> add(integer a, integer b) { return a + b; }
export function<integer> sub(integer a, integer b) { return a - b; }",
                },
                "main.code",
                "9\n5\n"
            ),
            (
                "module-lib-search-path",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import timesTwo from \"numbers.code\";\nprint(timesTwo(6));",
                    ["lib/numbers.code"] = "export function<integer> timesTwo(integer value) { return value * 2; }",
                },
                "main.code",
                "12\n"
            ),
            (
                "module-engine-wrapper-layer",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import everything as Draw from ""engine/drawing.code"";
import everything as Viewport from ""engine/viewport.code"";
import everything as Input from ""engine/input.code"";
import everything as Diagnostics from ""engine/diagnostics.code"";
import everything as Audio from ""engine/audio.code"";
import { rgb } from ""engine/colors.code"";
Draw.clearScreen(rgb(0, 0, 0));
Draw.rectangle(10, 10, 12, 14, rgb(255, 255, 255));
Draw.line(0, 0, 10, 10, rgb(255, 255, 255));
Draw.circle(20, 20, 8, rgb(255, 255, 255));
Draw.polygon({0, 0, 12, 0, 6, 12}, rgb(255, 255, 255));
Draw.image(""assets/test.svg"", 0, 0, 16, 16, 1);
Draw.sprite(""assets/test.svg"", 0, 0, 8, 8, 20, 20, 8, 8, 1);
Draw.text(""ok"", Viewport.hudWidth() - 10, 10, 12, ""right"", ""top"", rgb(255, 255, 255));
print(Viewport.hudWidth());
print(Input.keyIsDown(37));
print(Input.pointerWorldX());
print(Input.pointerWorldY());
print(Input.pointerScreenX());
print(Input.pointerScreenY());
print(Input.pointerIsDown());
print(Input.pointerWasPressed());
print(Input.pointerWasReleased());
print(Diagnostics.lastFrameIntervalMilliseconds());
print(Diagnostics.estimatedFramesPerSecond());
print(Diagnostics.lastFrameWorkMilliseconds());
print(Diagnostics.lastUpdateWorkMilliseconds());
print(Diagnostics.lastDrawWorkMilliseconds());
print(Diagnostics.lastDrawHudWorkMilliseconds());
print(Diagnostics.lastUpdateSteps());
print(Diagnostics.lastDroppedUpdateSteps());
print(Audio.canPlaySound());
print(Audio.playSound(""assets/click.wav"", 1));
print(Audio.playLoopingSound(""assets/loop.wav"", 1));
print(Audio.soundIsPlaying(1));
Audio.setSoundVolume(1, 1);
Audio.stopSound(1);
Audio.stopAllSounds();",
                    ["assets/test.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"></svg>",
                },
                "main.code",
                "640\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n"
            ),
            (
                "module-scene-canonical-import",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import { Scene, SceneLoop, WorldDrawable } from ""engine/scene.code"";

object Layer {
  constructor() { }

  implement WorldDrawable.draw() {
    print(""draw"");
  }
}

Scene scene = new Scene();
SceneLoop loop = new SceneLoop(scene);
scene.addWorldDrawable(new Layer(), 0);
loop.start();
loop.draw();",
                },
                "main.code",
                "draw\n"
            ),
            (
                "module-scene-loop-layered-draw-order",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import { SceneLoop } from ""engine/loop.code"";
import { Scene, Updatable, WorldDrawable } from ""engine/scene.code"";

object Lower {
  constructor() { }
  function draw() {
    print(""lower"");
  }
}

object Upper {
  constructor() { }
  function draw() {
    print(""upper"");
  }
}

object Counter {
  integer updates;

  constructor() {
    updates = 0;
  }

  function update() {
    updates += 1;
    print(""update="" + updates);
  }
}

implement WorldDrawable for Lower {
  draw() via Lower.draw;
}

implement WorldDrawable for Upper {
  draw() via Upper.draw;
}

implement Updatable for Counter {
  update() via Counter.update;
}

Scene scene = new Scene();
SceneLoop loop = new SceneLoop(scene);
scene.addWorldDrawable(new Upper(), 10);
scene.addWorldDrawable(new Lower(), 0);
scene.addUpdatable(new Counter());
loop.start();
loop.update();
loop.draw();",
                },
                "main.code",
                "update=1\nlower\nupper\n"
            ),
            (
                "module-scene-loop-staged-add-and-start",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import { SceneLoop } from ""engine/loop.code"";
import { Scene, Startable, Updatable } from ""engine/scene.code"";

object Child {
  constructor() { }

  function start() {
    print(""child-start"");
  }

  function update() {
    print(""child-update"");
  }
}

object Spawner {
  Scene scene;
  Child child;
  boolean spawned;

  constructor(Scene scene, Child child) {
    this.scene = scene;
    this.child = child;
    spawned = false;
  }

  function update() {
    print(""spawner-update"");
    if not spawned then {
      scene.addStartable(child);
      scene.addUpdatable(child);
      spawned = true;
    }
  }
}

implement Startable for Child {
  start() via Child.start;
}

implement Updatable for Child {
  update() via Child.update;
}

implement Updatable for Spawner {
  update() via Spawner.update;
}

Scene scene = new Scene();
SceneLoop loop = new SceneLoop(scene);
Child child = new Child();
Spawner spawner = new Spawner(scene, child);
scene.addUpdatable(spawner);
loop.start();
loop.update();
loop.update();",
                },
                "main.code",
                "spawner-update\nchild-start\nspawner-update\nchild-update\n"
            ),
            (
                "module-scene-loop-staged-remove",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import { SceneLoop } from ""engine/loop.code"";
import { Scene, Updatable } from ""engine/scene.code"";

object Child {
  constructor() { }
  function update() {
    print(""child"");
  }
}

object Remover {
  Scene scene;
  Child child;
  boolean removed;

  constructor(Scene scene, Child child) {
    this.scene = scene;
    this.child = child;
    removed = false;
  }

  function update() {
    if not removed then {
      scene.removeUpdatable(child);
      removed = true;
    }
    print(""remover"");
  }
}

implement Updatable for Child {
  update() via Child.update;
}

implement Updatable for Remover {
  update() via Remover.update;
}

Scene scene = new Scene();
SceneLoop loop = new SceneLoop(scene);
Child child = new Child();
Remover remover = new Remover(scene, child);
scene.addUpdatable(child);
scene.addUpdatable(remover);
loop.start();
loop.update();
loop.update();",
                },
                "main.code",
                "child\nremover\nremover\n"
            ),
            (
                "module-import-object-interface",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Counter from ""types.code"";
import IValue from ""types.code"";
IValue value = new Counter(9);
print(value.read());",
                    ["types.code"] =
@"export interface IValue {
  function<integer> read();
}
export object Counter {
  integer count;
  constructor(integer n) {
    this.count = n;
  }
  function<integer> read() {
    return this.count;
  }
}
implement IValue for Counter {
  read() via Counter.read;
}",
                },
                "main.code",
                "9\n"
            ),
            (
                "module-alias-object-interface",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Counter as LocalCounter from ""types.code"";
import IValue as LocalValue from ""types.code"";
LocalValue value = new LocalCounter(4);
print(value.read());",
                    ["types.code"] =
@"export interface IValue {
  function<integer> read();
}
export object Counter {
  integer count;
  constructor(integer n) {
    this.count = n;
  }
  function<integer> read() {
    return this.count;
  }
}
implement IValue for Counter {
  read() via Counter.read;
}",
                },
                "main.code",
                "4\n"
            ),
            (
                "module-package-declaration",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.main;
import add from ""math.code"";
print(add(1, 2));",
                    ["math.code"] =
@"package app.math;
export function<integer> add(integer a, integer b) {
  return a + b;
}",
                },
                "main.code",
                "3\n"
            ),
            (
                "module-import-enum-alias",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Direction as Heading from ""types.code"";
Heading heading = Heading.Right;
print(heading == Heading.Right);",
                    ["types.code"] =
@"export enum Direction {
  Left;
  Right;
}",
                },
                "main.code",
                "1\n"
            ),
            (
                "module-re-export-enum",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Direction from ""api.code"";
Direction direction = Direction.Left;
print(direction == Direction.Left);",
                    ["api.code"] = "export import Direction from \"types.code\";",
                    ["types.code"] =
@"export enum Direction {
  Left;
  Right;
}",
                },
                "main.code",
                "1\n"
            ),
            (
                "module-package-manifest-valid",
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""exports"": { ""math"": ""math.code"" },
  ""targets"": [""vm-native"", ""vm-web""],
  ""dependencies"": { ""std.core"": ""^0.1.0"" },
  ""devDependencies"": { ""test.assert"": ""^0.1.0"" },
  ""targetOverrides"": {
    ""vm-web"": { ""entry"": ""main_web.code"" }
  },
  ""hostAbi"": {
    ""requires"": [""std.time""]
  }
}",
                    ["main.code"] = "print(7);",
                    ["main_web.code"] = "print(8);",
                    ["math.code"] = "export function<integer> noop() { return 0; }",
                    ["packages/std.core/code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""std.core"",
  ""version"": ""0.1.3"",
  ""kind"": ""library"",
  ""entry"": ""src/main.code"",
  ""targets"": [""vm-native"", ""vm-web""]
}",
                    ["packages/std.core/src/main.code"] = "print(0);",
                },
                "main.code",
                "7\n"
            )
        };
        var moduleToolingCases = new List<(string Name, IReadOnlyDictionary<string, string> Files, string Entry, string[] GraphContains, string[] JsonContains, string[] DotContains, string[] TraceContains)>
        {
            (
                "module-graph-tooling",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import { add, sub as minus } from \"math.code\";\nprint(add(7, 2));\nprint(minus(7, 2));",
                    ["math.code"] =
@"package app.math;
export function<integer> add(integer a, integer b) { return a + b; }
export function<integer> sub(integer a, integer b) { return a - b; }",
                },
                "main.code",
                new[]
                {
                    "Entry: main.code",
                    "Target: vm-native",
                    "Capabilities: standard.input_output",
                    "main.code -> math.code",
                    "{ add, sub as minus } from \"math.code\"",
                    "math.code package=app.math exports=add, sub"
                },
                new[]
                {
                    "\"entry\": \"main.code\"",
                    "\"target\": \"vm-native\"",
                    "\"requiredCapabilities\": [",
                    "\"standard.input_output\"",
                    "\"path\": \"math.code\"",
                    "\"package\": \"app.math\"",
                    "\"binding\": \"{ add, sub as minus }\""
                },
                new[]
                {
                    "digraph ModuleGraph {",
                    "shape=doubleoctagon",
                    "main.code",
                    "math.code",
                    "add, sub as minus"
                },
                new[]
                {
                    "Link entry module main.code",
                    "Resolve import { add, sub as minus } from \"math.code\" in main.code -> math.code",
                    "Linked main.code"
                }
            ),
            (
                "module-graph-manifest-capabilities",
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""hostAbi"": {
    ""requires"": [""std.time"", ""std.io""]
  }
}",
                    ["main.code"] = "print(1);",
                },
                "main.code",
                new[]
                {
                    "Entry: main.code",
                    "Target: vm-native",
                    "Capabilities: standard.input_output, std.time"
                },
                new[]
                {
                    "\"entry\": \"main.code\"",
                    "\"target\": \"vm-native\"",
                    "\"requiredCapabilities\": [",
                    "\"standard.input_output\"",
                    "\"std.time\""
                },
                new[]
                {
                    "digraph ModuleGraph {",
                    "main.code"
                },
                new[]
                {
                    "Load manifest code.package.json name=demo.app target=vm-native",
                    "Capability required: std.time",
                    "Capability required: standard.input_output"
                }
            ),
            (
                "module-graph-hostcall-capabilities",
                new Dictionary<string, string>
                {
                    ["main.code"] = "print(unixMilliseconds());",
                },
                "main.code",
                new[]
                {
                    "Entry: main.code",
                    "Capabilities: standard.input_output, std.time"
                },
                new[]
                {
                    "\"requiredCapabilities\": [",
                    "\"standard.input_output\"",
                    "\"std.time\""
                },
                new[]
                {
                    "digraph ModuleGraph {",
                    "main.code"
                },
                new[]
                {
                    "Capability required: standard.input_output",
                    "Capability required: std.time"
                }
            ),
            (
                "module-graph-hostcall-native-only-capabilities",
                new Dictionary<string, string>
                {
                    ["main.code"] = "sleepMilliseconds(0);\nprint(readLine());",
                },
                "main.code",
                new[]
                {
                    "Entry: main.code",
                    "Capabilities: standard.input_output, standard.input_output.read_line, std.time.sleep_ms"
                },
                new[]
                {
                    "\"requiredCapabilities\": [",
                    "\"standard.input_output\"",
                    "\"standard.input_output.read_line\"",
                    "\"std.time.sleep_ms\""
                },
                new[]
                {
                    "digraph ModuleGraph {",
                    "main.code"
                },
                new[]
                {
                    "Capability required: standard.input_output",
                    "Capability required: standard.input_output.read_line",
                    "Capability required: std.time.sleep_ms"
                }
            )
        };
        var moduleTargetCases = new List<(string Name, Compiler.CompileTarget Target, IReadOnlyDictionary<string, string> Files, string Entry, string Expected)>
        {
            (
                "module-target-web-allows-std-time",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["main.code"] = "import nowMs from \"std/time.code\";\nprint(nowMs());",
                    ["lib/std/time.code"] =
@"package std.time;
export function<integer> nowMs() { return 1; }",
                },
                "main.code",
                "1\n"
            ),
            (
                "module-target-native-allows-std-fs",
                Compiler.CompileTarget.VmNative,
                new Dictionary<string, string>
                {
                    ["main.code"] = "import readText from \"std/fs.code\";\nprint(readText());",
                    ["lib/std/fs.code"] =
@"package std.fs;
export function<string> readText() { return ""ok""; }",
                },
                "main.code",
                "ok\n"
            ),
            (
                "module-target-web-manifest-parse-overrides",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""targets"": [""vm-native"", ""vm-web""],
  ""targetOverrides"": {
    ""vm-web"": { ""entry"": ""main_web.code"" }
  }
}",
                    ["main.code"] = "print(7);",
                    ["main_web.code"] = "print(8);",
                },
                "main.code",
                "7\n"
            )
        };
        var moduleLockfileCases = new List<(string Name, Compiler.CompileTarget Target, IReadOnlyDictionary<string, string> Files, string Entry, string[] LockContains)>
        {
            (
                "module-lockfile-generated",
                Compiler.CompileTarget.VmNative,
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""dependencies"": {
    ""math.core"": ""^1.0.0""
  }
}",
                    ["main.code"] = "print(1);",
                    ["packages/math.core/code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""math.core"",
  ""version"": ""1.2.0"",
  ""kind"": ""library"",
  ""entry"": ""src/main.code""
}",
                    ["packages/math.core/src/main.code"] = "print(0);",
                },
                "main.code",
                new[]
                {
                    "\"schemaVersion\": 1",
                    "\"target\": \"vm-native\"",
                    "\"name\": \"demo.app\"",
                    "\"name\": \"math.core\"",
                    "\"version\": \"1.2.0\"",
                    "\"resolved\": \"packages/math.core/code.package.json\"",
                    "\"integrity\": \"sha256-"
                }
            )
        };
        var moduleArtifactCases = new List<(string Name, Compiler.CompileTarget Target, IReadOnlyDictionary<string, string> Files, string Entry, string[] ArtifactContains, string[] LockContains, string ExpectedRunOutput)>
        {
            (
                "module-codelib-generated",
                Compiler.CompileTarget.VmNative,
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.lib"",
  ""version"": ""0.2.0"",
  ""kind"": ""library"",
  ""entry"": ""main.code"",
  ""hostAbi"": {
    ""requires"": [""standard.input_output""]
  }
}",
                    ["main.code"] = "print(11); export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                new[]
                {
                    "\"schemaVersion\": 1",
                    "\"name\": \"demo.lib\"",
                    "\"version\": \"0.2.0\"",
                    "\"kind\": \"library\"",
                    "\"target\": \"vm-native\"",
                    "\"requiredCapabilities\": [",
                    "\"standard.input_output\"",
                    "\"bytecode\": \""
                },
                new[]
                {
                    "\"name\": \"demo.lib\"",
                    "\"resolved\": \"demo-lib-0.2.0-vm-native.codelib\"",
                    "\"integrity\": \"sha256-"
                },
                "11\n"
            )
        };
        // Expected error cases
        var errorCases = new List<(string Name, string Source, string ExpectedType)>
        {
            ("panic-basic", @"panic(""boom"");", "UserError"),
            ("map-missing-key-runtime",
@"map<string, integer> scores = new map<string, integer>();
print(scores[""coins""]);", "RuntimeError"),
            ("queue-empty-runtime",
@"queue<integer> items = new queue<integer>();
print(items.dequeue());", "RuntimeError"),
            ("stack-empty-runtime",
@"stack<integer> items = new stack<integer>();
print(items.peek());", "RuntimeError"),
            ("cast-negative-whole-runtime",
@"print(-1 as whole);", "RuntimeError"),
            ("cast-integer-out-of-range-runtime",
@"print(2147483648. as integer);", "RuntimeError"),
            ("cast-byte-out-of-range-runtime",
@"print(300 as byte);", "RuntimeError"),
            ("cast-whole16-negative-runtime",
@"print(-1 as whole16);", "RuntimeError"),
            ("cast-integer16-out-of-range-runtime",
@"print(32768 as integer16);", "RuntimeError"),
            ("sized-compound-assignment-out-of-range-runtime",
@"byte value = 250;
value += 6;
print(value);", "RuntimeError"),
        };
        var compileErrorCases = new List<(string Name, string Source, string ErrorContains)>
        {
            ("object-duplicate-name", @"object Person { integer age; constructor(integer v){ this.age = v; } } object Person { integer score; constructor(integer v){ this.score = v; } }", "already defined"),
            ("object-duplicate-field", @"object Person { integer age; integer age; constructor(integer v){ this.age = v; } }", "already defined"),
            ("object-unknown-field-type", @"object Person { UnknownType data; }", "Unknown type"),
            ("object-missing-constructor", @"object Person { integer age; }", "has no constructor"),
            ("object-missing-constructor-for-partial-defaults", @"object Person { integer age = 1; string name; }", "has no constructor"),
            ("object-missing-field-init", @"object Person { integer age; constructor() { } }", "does not definitely assign fields"),
            ("object-field-default-type-mismatch", @"object Person { integer age = ""old""; }", "Field initializer type mismatch"),
            ("object-field-default-cannot-read-field", @"object Person { integer age = 1; integer next = age + 1; }", "Undefined variable 'age'"),
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
            ("object-member-private-field-external",
@"object Box {
  private integer amount;
  public constructor() {
    amount = 1;
  }
}
Box box = new Box();
print(box.amount);", "Field 'Box.amount' is not accessible"),
            ("object-member-private-method-external",
@"object Box {
  public constructor() { }
  private function<integer> hidden() {
    return 1;
  }
}
Box box = new Box();
print(box.hidden());", "Method 'Box.hidden' is not accessible"),
            ("object-member-private-constructor-external",
@"object Secret {
  private constructor() { }
}
Secret secret = new Secret();", "Constructor for 'Secret' is not accessible"),
            ("object-member-package-requires-package",
@"object Box {
  package integer amount;
  public constructor() {
    amount = 1;
  }
}", "Package-visible members require a containing package declaration"),
            ("object-implicit-this-method-no-fallback",
@"function<integer> move() {
  return 100;
}
object Walker {
  constructor() { }
  function move(integer steps) {
  }
  function<integer> read() {
    return move();
  }
}
Walker w = new Walker();
print(w.read());", "no matching method overload"),
            ("object-implicit-this-undefined-still-errors",
@"object Walker {
  constructor() { }
  function<integer> read() {
    return speed;
  }
}
Walker w = new Walker();
print(w.read());", "Undefined variable"),
            ("constant-reassign",
@"constant integer maxLives = 3;
maxLives = 4;", "Cannot assign to constant 'maxLives'"),
            ("global-constant-compound-reassign",
@"constant integer limit = 3;
function bump() {
  limit += 1;
}
bump();", "Cannot assign to constant 'limit'"),
            ("global-constant-postfix-reassign",
@"constant integer limit = 3;
function bump() {
  limit++;
}
bump();", "Cannot assign to constant 'limit'"),
            ("built-in-constant-reassign",
@"pi = 4.;", "Cannot assign to constant 'pi'"),
            ("constant-missing-init", @"constant integer value;", "must be initialized"),
            ("time-intrinsic-arity", @"print(unixMilliseconds(1));", "expects 0 args"),
            ("math-intrinsic-arity", @"print(minimum(1));", "expects 2 args"),
            ("old-read-line-name-rejected", @"print(read_line());", "Undefined function 'read_line'"),
            ("old-sleep-ms-name-rejected", @"sleep_ms(0);", "Undefined function 'sleep_ms'"),
            ("old-remove-at-name-rejected", @"array<integer> items = {1}; items.remove_at(0);", "Array has no method 'remove_at'"),
            ("old-key-down-name-rejected", @"print(key_down(37));", "Undefined function 'key_down'"),
            ("fallible-type-argument-arity",
@"function<fallible<integer, integer, integer>> parse() {
  return 1;
}", "expects one or two type arguments"),
            ("fallible-error-code-type",
@"function<fallible<integer, string>> parse() {
  return 1;
}", "fallible error code type must be an enum or integer"),
            ("fallible-void-success-deferred",
@"enum LoadError {
  Missing;
}
function<fallible<void, LoadError>> load() {
  return error(LoadError.Missing);
}", "fallible success type cannot be void"),
            ("fallible-message-only-error-rejected",
@"enum LoadError {
  Missing;
}
function<fallible<integer, LoadError>> load() {
  return error(""missing"");
}", "error(message) is only valid for fallible<Value> or fallible<Value, integer>"),
            ("fallible-error-outside-fallible-function",
@"integer value = error(1);", "error(...)' is only valid"),
            ("fallible-on-error-non-fallible",
@"integer value = 1 on error {
  yield 0;
};", "requires a fallible value"),
            ("fallible-yield-wrong-type",
@"enum LoadError {
  Missing;
}
function<fallible<integer, LoadError>> load() {
  return error(LoadError.Missing);
}
integer value = load() on error {
  yield ""missing"";
};", "Yield value type mismatch"),
            ("fallible-bare-error-value-rejected",
@"enum LoadError {
  Missing;
}
function<fallible<integer, LoadError>> load() {
  return error(LoadError.Missing);
}
integer value = load() on error {
  print(error);
  yield 0;
};", "Use error.code or error.message"),
            ("fallible-yield-outside-handler",
@"yield 1;", "yield' is only valid"),
            ("break-outside-loop",
@"break;", "break' is only valid"),
            ("continue-outside-loop",
@"continue;", "continue' is only valid"),
            ("binary-prefix-invalid-digit",
@"print(0b102);", "Invalid digit"),
            ("hex-prefix-missing-digits",
@"print(0x);", "Invalid hexadecimal integer literal"),
            ("real-exponent-deferred",
@"print(1e3);", "Invalid integer literal suffix"),
            ("real-suffix-deferred",
@"print(1.5r32);", "Invalid real literal suffix"),
            ("integer-suffix-deferred",
@"print(1i32);", "Invalid integer literal suffix"),
            ("cast-unsupported-target",
@"print(1 as string);", "Cast target must be a numeric type or an enum type"),
            ("byte-literal-out-of-range-high",
@"byte bad = 256;", "Initializer type mismatch"),
            ("byte-literal-out-of-range-negative",
@"byte bad = -1;", "Initializer type mismatch"),
            ("integer8-literal-out-of-range-high",
@"integer8 bad = 128;", "Initializer type mismatch"),
            ("integer8-literal-out-of-range-low",
@"integer8 bad = -129;", "Initializer type mismatch"),
            ("sized-numeric-dynamic-narrowing-requires-cast",
@"integer value = 5;
byte narrowed = value;", "Initializer type mismatch"),
            ("integer64-deferred",
@"integer64 value = 0;", "Unknown type"),
            ("whole64-deferred",
@"whole64 value = 0;", "Unknown type"),
            ("integer-literal-beyond-v1-range",
@"whole32 value = 4294967296;", "Invalid integer literal"),
            ("cast-invalid-enum-literal",
@"enum Direction {
  Left = 1;
}
Direction direction = 2 as Direction;", "not a declared value"),
            ("cast-enum-to-real-requires-integer",
@"enum Direction {
  Left;
}
print(Direction.Left as real);", "Enum casts are only supported between enum values and integer"),
            ("void-return-value",
@"function<void> nope() {
  return 1;
}", "Void function cannot return a value"),
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
            ("interface-inline-and-external-duplicate",
@"interface IThing {
  function<integer> id();
}
object Thing {
  constructor() { }
  implement IThing.id() {
    return 1;
  }
}
implement IThing for Thing {
  id() via Thing.id;
}", "mapped more than once"),
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
            ("enum-init-from-integer-mismatch",
@"enum Direction {
  Left;
  Right;
}
Direction direction = 0;", "Initializer type mismatch"),
            ("enum-member-assignment-forbidden",
@"enum Direction {
  Left;
}
Direction.Left = Direction.Left;", "Enum members are constants"),
            ("enum-unknown-member",
@"enum Direction {
  Left;
  Right;
}
Direction direction = Direction.Up;", "has no member"),
            ("switch-case-type-mismatch",
@"switch 1 then {
  case ""one"" then print(1);
  default then print(2);
}", "Switch case value type must be comparable to switch value"),
            ("switch-default-duplicate",
@"switch 1 then {
  case 1 then print(1);
  default then print(2);
  default then print(3);
}", "already has a 'default'"),
            ("switch-case-after-default",
@"switch 1 then {
  default then print(2);
  case 1 then print(1);
}", "cannot appear after 'default'"),
            ("switch-empty",
@"switch 1 then {
}", "at least one 'case' or 'default'"),
            ("map-type-argument-arity",
@"map<integer> values = new map<integer>();", "expects exactly two type arguments"),
            ("map-key-type-mismatch",
@"map<string, integer> scores = new map<string, integer>();
scores[1] = 2;", "Map key type mismatch"),
            ("queue-enqueue-arity",
@"queue<integer> items = new queue<integer>();
items.enqueue();", "expects 1 argument"),
            ("stack-push-type-mismatch",
@"stack<integer> items = new stack<integer>();
items.push(""oops"");", "Stack element type mismatch"),
            ("record-nonhashable-set-element",
@"record Stats {
  array<integer> history;
  constructor() {
    history = {1, 2};
  }
}
set<Stats> items = new set<Stats>();", "must be hashable"),
            ("record-nonhashable-map-key",
@"record Stats {
  array<integer> history;
  constructor() {
    history = {1, 2};
  }
}
map<Stats, integer> values = new map<Stats, integer>();", "must be hashable"),
            ("record-nonhashable-equality",
@"record Stats {
  array<integer> history;
  constructor() {
    history = {1, 2};
  }
}
Stats left = new Stats();
Stats right = new Stats();
print(left == right);", "Equality requires compatible types"),
            ("record-cycle-not-supported",
@"record Node {
  optional<Node> next;
  constructor() {
    next = none;
  }
}", "cannot contain itself by value"),
        };
        var moduleErrorCases = new List<(string Name, IReadOnlyDictionary<string, string> Files, string Entry, string ErrorContains)>
        {
            (
                "module-import-missing-export",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import hidden from \"lib.code\";\nprint(hidden());",
                    ["lib.code"] = "function<integer> hidden() { return 1; }",
                },
                "main.code",
                "does not export"
            ),
            (
                "module-import-private-declaration",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import hidden from \"lib.code\";\nprint(hidden());",
                    ["lib.code"] = "private function<integer> hidden() { return 1; }",
                },
                "main.code",
                "does not export 'hidden'"
            ),
            (
                "module-import-package-declaration-cross-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.main;
import helper from ""lib.code"";
print(helper(1));",
                    ["lib.code"] =
@"package app.shared;
package function<integer> helper(integer value) { return value + 1; }",
                },
                "main.code",
                "package-visible"
            ),
            (
                "module-package-visibility-requires-package-declaration",
                new Dictionary<string, string>
                {
                    ["main.code"] = "package function<integer> helper(integer value) { return value + 1; }",
                },
                "main.code",
                "require a preceding package declaration"
            ),
            (
                "module-global-not-visible-cross-module",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import Helper from ""helper.code"";
Helper helper = new Helper();
print(shared);",
                    ["helper.code"] =
@"integer shared = 4;

export object Helper {
  constructor() {
  }
}",
                },
                "main.code",
                "Undefined variable 'shared'"
            ),
            (
                "module-member-package-field-cross-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.main;
import Box from ""box.code"";
Box box = new Box();
print(box.amount);",
                    ["box.code"] =
@"package app.shared;
public object Box {
  package integer amount;
  public constructor() {
    amount = 1;
  }
}",
                },
                "main.code",
                "Field 'Box.amount' is not accessible"
            ),
            (
                "module-member-package-method-cross-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.main;
import Box from ""box.code"";
Box box = new Box();
print(box.read());",
                    ["box.code"] =
@"package app.shared;
public object Box {
  public constructor() { }
  package function<integer> read() {
    return 1;
  }
}",
                },
                "main.code",
                "Method 'Box.read' is not accessible"
            ),
            (
                "module-member-package-constructor-cross-package",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.main;
import Box from ""box.code"";
Box box = new Box();",
                    ["box.code"] =
@"package app.shared;
public object Box {
  package constructor() { }
}",
                },
                "main.code",
                "Constructor for 'Box' is not accessible"
            ),
            (
                "module-public-reexport-package-visible-rejected",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package demo.pkg;
export import helper from ""helper.code"";",
                    ["helper.code"] =
@"package demo.pkg;
package function<integer> helper(integer value) { return value + 1; }",
                },
                "main.code",
                "Cannot publicly re-export non-public declaration"
            ),
            (
                "module-grouped-import-missing-export",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import { add, nope } from \"math.code\";\nprint(add(1,2));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "does not export 'nope'"
            ),
            (
                "module-import-cycle",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import run from \"a.code\";\nprint(run());",
                    ["a.code"] = "import runB from \"b.code\";\nexport function<integer> run() { return runB(); }",
                    ["b.code"] = "import run from \"a.code\";\nexport function<integer> runB() { return run(); }",
                },
                "main.code",
                "Circular import detected"
            ),
            (
                "module-namespace-import-runtime-value-error",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import everything as Math from ""math.code"";
print(Math);",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "cannot be used as a runtime value"
            ),
            (
                "module-namespace-import-missing-member",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import everything as Math from ""math.code"";
print(Math.mul(2, 3));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "does not export function 'mul'"
            ),
            (
                "module-package-duplicate",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"package app.one;
package app.two;
print(1);",
                },
                "main.code",
                "Only one package declaration"
            ),
            (
                "module-package-ordering",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import add from ""math.code"";
package app.bad;
print(add(1, 2));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "Package declaration must appear before imports and declarations"
            ),
            (
                "module-import-binding-collision",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import add from ""math.code"";
import add from ""other.code"";
print(add(1, 2));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                    ["other.code"] = "export function<integer> add(integer a, integer b) { return a - b; }",
                },
                "main.code",
                "Import binding 'add' is already declared"
            ),
            (
                "module-decl-import-collision",
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import add from ""math.code"";
function<integer> add(integer a, integer b) { return a * b; }
print(add(2, 3));",
                    ["math.code"] = "export function<integer> add(integer a, integer b) { return a + b; }",
                },
                "main.code",
                "conflicts with an import binding"
            ),
            (
                "module-missing-export-chain",
                new Dictionary<string, string>
                {
                    ["main.code"] = "import run from \"a.code\";\nprint(run());",
                    ["a.code"] = "import hidden from \"b.code\";\nexport function<integer> run() { return hidden(); }",
                    ["b.code"] = "function<integer> hidden() { return 1; }",
                },
                "main.code",
                "Import chain: main.code -> a.code -> b.code"
            ),
            (
                "module-manifest-missing-field",
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code""
}",
                    ["main.code"] = "print(1);",
                },
                "main.code",
                "Missing required field 'name'"
            ),
            (
                "module-manifest-invalid-json",
                new Dictionary<string, string>
                {
                    ["code.package.json"] = "{ invalid json }",
                    ["main.code"] = "print(1);",
                },
                "main.code",
                "Invalid JSON"
            ),
            (
                "module-lockfile-version-mismatch",
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""dependencies"": {
    ""math.core"": ""^2.0.0""
  }
}",
                    ["main.code"] = "print(1);",
                    ["packages/math.core/code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""math.core"",
  ""version"": ""1.2.0"",
  ""kind"": ""library"",
  ""entry"": ""src/main.code""
}",
                    ["packages/math.core/src/main.code"] = "print(0);",
                },
                "main.code",
                "does not satisfy version range '^2.0.0'"
            ),
            (
                "module-lockfile-invalid-codelib",
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.app"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""dependencies"": {
    ""math.core"": ""^1.0.0""
  }
}",
                    ["main.code"] = "print(1);",
                    ["packages/math.core/code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""math.core"",
  ""version"": ""1.2.0"",
  ""kind"": ""library"",
  ""entry"": ""src/main.code""
}",
                    ["packages/math.core/src/main.code"] = "print(0);",
                    ["packages/math.core/math-core-1.2.0-vm-native.codelib"] = "{ bad json }",
                },
                "main.code",
                "Library artifact 'math-core-1.2.0-vm-native.codelib'"
            ),
        };
        var moduleTargetErrorCases = new List<(string Name, Compiler.CompileTarget Target, IReadOnlyDictionary<string, string> Files, string Entry, string ErrorContains)>
        {
            (
                "module-target-web-rejects-std-fs",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["main.code"] = "import readText from \"std/fs.code\";\nprint(readText());",
                    ["lib/std/fs.code"] =
@"package std.fs;
export function<string> readText() { return ""ok""; }",
                },
                "main.code",
                "Capability 'std.fs' is not available for target 'vm-web'"
            ),
            (
                "module-target-web-rejects-read-line-intrinsic",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["main.code"] = "print(readLine());",
                },
                "main.code",
                "Capability 'standard.input_output.read_line' is not available for target 'vm-web'"
            ),
            (
                "module-target-web-rejects-sleep-ms-intrinsic",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["main.code"] = "sleepMilliseconds(1); print(1);",
                },
                "main.code",
                "Capability 'std.time.sleep_ms' is not available for target 'vm-web'"
            ),
            (
                "module-target-web-rejects-manifest-unsupported-target",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.native"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""targets"": [""vm-native""]
}",
                    ["main.code"] = "print(1);",
                },
                "main.code",
                "does not support target 'vm-web'"
            ),
            (
                "module-target-web-rejects-manifest-host-requirement",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["code.package.json"] =
@"{
  ""schemaVersion"": 1,
  ""name"": ""demo.fs"",
  ""version"": ""0.1.0"",
  ""kind"": ""application"",
  ""entry"": ""main.code"",
  ""hostAbi"": {
    ""requires"": [""std.fs""]
  }
}",
                    ["main.code"] = "print(1);",
                },
                "main.code",
                "hostAbi.requires"
            )
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

        foreach (var (name, files, entry, expected) in moduleCases)
        {
            try
            {
                var output = Normalize(CompileAndRunModules(files, entry));
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

        foreach (var (name, target, files, entry, expected) in moduleTargetCases)
        {
            try
            {
                var output = Normalize(CompileAndRunModules(files, entry, target));
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

        foreach (var (name, target, files, entry, lockContains) in moduleLockfileCases)
        {
            try
            {
                string lockfile = CompileModulesAndReadLockfile(files, entry, target);
                if (!ContainsAll(name, "lockfile", lockfile, lockContains))
                {
                    failures++;
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

        foreach (var (name, target, files, entry, artifactContains, lockContains, expectedRunOutput) in moduleArtifactCases)
        {
            try
            {
                var outputs = CompileModulesAndReadArtifact(files, entry, target);
                bool matched =
                    ContainsAll(name, "artifact", outputs.ArtifactJson, artifactContains) &&
                    ContainsAll(name, "lockfile", outputs.LockfileJson, lockContains) &&
                    string.Equals(Normalize(outputs.RunOutput), Normalize(expectedRunOutput), StringComparison.Ordinal);

                if (!matched)
                {
                    if (!string.Equals(Normalize(outputs.RunOutput), Normalize(expectedRunOutput), StringComparison.Ordinal))
                    {
                        Console.WriteLine($"[FAIL] {name}: run output expected '{Escape(Normalize(expectedRunOutput))}' got '{Escape(Normalize(outputs.RunOutput))}'");
                    }
                    failures++;
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

        foreach (var (name, files, entry, graphContains, jsonContains, dotContains, traceContains) in moduleToolingCases)
        {
            try
            {
                var outputs = CompileModulesWithMetadata(files, entry);
                bool matched =
                    ContainsAll(name, "graph", outputs.GraphText, graphContains) &&
                    ContainsAll(name, "graph-json", outputs.GraphJson, jsonContains) &&
                    ContainsAll(name, "graph-dot", outputs.GraphDot, dotContains) &&
                    ContainsAll(name, "trace", outputs.TraceOutput, traceContains);

                if (!matched)
                {
                    failures++;
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

        foreach (var (name, files, entry, errorContains) in moduleErrorCases)
        {
            try
            {
                CompileModulesExpectError(files, entry, errorContains);
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: {ex.GetType().Name} - {ex.Message}");
            }
        }

        foreach (var (name, target, files, entry, errorContains) in moduleTargetErrorCases)
        {
            try
            {
                CompileModulesExpectError(files, entry, errorContains, target);
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

    private static int RunWebBuildTests()
    {
        int failures = 0;

        void ExpectWebBuildCompilerError(string testName, IReadOnlyDictionary<string, string> files, string entryRelativePath, string expectedContains)
        {
            try
            {
                _ = BuildWebApp(files, entryRelativePath);
                failures++;
                Console.WriteLine($"[FAIL] {testName}: expected compile error");
            }
            catch (Compiler.CompilerException ex)
            {
                if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {testName}: unexpected error '{ex.Message}'");
                }
                else
                {
                    Console.WriteLine($"[PASS] {testName}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {testName}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["assets/code-sheet.svg"] =
"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"32\"><rect width=\"32\" height=\"32\" fill=\"#0b1020\"/><rect x=\"32\" width=\"32\" height=\"32\" fill=\"#1d4ed8\"/></svg>",
                    ["main.code"] =
@"integer start_x = 100;
integer x = start_x;
integer y = initial_y();
integer speed = 2;
constant integer hud_margin = 16;

function<integer> initial_y() {
  return 120;
}

function start() {
}

function update() {
  if Input.keyIsDown(37) then x -= speed;
  if Input.keyIsDown(39) then x += speed;
  if Input.keyIsDown(38) then y -= speed;
  if Input.keyIsDown(40) then y += speed;
  if Input.pointerWasPressed() then {
    x = Input.pointerWorldX() as integer;
    y = Input.pointerWorldY() as integer;
  }
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
  Draw.line(Viewport.safeLeft(), Viewport.safeTop(), Viewport.safeRight(), Viewport.safeBottom(), Colors.rgb(255, 255, 255));
  Draw.polygon({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, Colors.rgb(255, 255, 255));
  Draw.circle(124, 84, 16, Colors.rgb(255, 255, 255));
  Draw.image(""assets/code-sheet.svg"", 24, 220, 64, 32, 1);
  Draw.sprite(""assets/code-sheet.svg"", 32, 0, 32, 32, 104, 210, 64, 64, 1);
  if x > Viewport.viewLeft() - 24 and x < Viewport.viewRight() then {
    Draw.rectangle(x, y, 24, 24, Colors.rgb(255, 255, 255));
  }
}

function drawHud() {
  Draw.text(""Code"", hud_margin, hud_margin, 18, ""left"", ""top"", Colors.rgb(255, 255, 255));
  Draw.text(""Arrow keys move"", Viewport.hudWidth() - hud_margin, hud_margin, 16, ""right"", ""top"", Colors.rgb(255, 255, 255));
  Draw.text(""Pointer: {Input.pointerScreenX()}, {Input.pointerScreenY()}"", hud_margin, 40, 14, ""left"", ""top"", Colors.rgb(255, 255, 255));
  Draw.text(""Frame work: {Diagnostics.lastFrameWorkMilliseconds()}"", hud_margin, 64, 14, ""left"", ""top"", Colors.rgb(255, 255, 255));
  Draw.text(""Audio ready: {Audio.canPlaySound()}"", hud_margin, 88, 14, ""left"", ""top"", Colors.rgb(255, 255, 255));
}"
                },
                "main.code");

            bool matched =
                string.Equals(Path.GetFileName(outputs.OutputDirectory), "dist", StringComparison.OrdinalIgnoreCase) &&
                outputs.IndexHtmlExists &&
                !outputs.BytecodeExists &&
                outputs.BytecodeLength == 0 &&
                outputs.OutputFiles.Any(path => string.Equals(
                    path.Replace('\\', '/'),
                    "assets/code-sheet.svg",
                    StringComparison.OrdinalIgnoreCase)) &&
                outputs.OutputFiles.Any(path => string.Equals(
                    path.Replace('\\', '/'),
                    "code-runtime.wasm",
                    StringComparison.OrdinalIgnoreCase)) &&
                outputs.IndexHtml.Contains("CanvasSceneRuntime", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("APP_METADATA", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("APP_BYTECODE_BASE64", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("CODE_RUNTIME_WASM_BASE64", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("WasmWebVm", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("CODE_WORKER_SOURCE", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("new WorkerCodeRuntimeController", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("new Worker(this.workerUrl", StringComparison.Ordinal) &&
                !outputs.IndexHtml.Contains("output: line => runtime.appendOutput(line)", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("this.appControlKeyCodes = new Set([32, 33, 34, 35, 36, 37, 38, 39, 40])", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("event.preventDefault()", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("touchAction = \"none\"", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("pointerdown", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("engine.input.pointer_world_x_scene", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("engine.diagnostics.last_frame_work_milliseconds_scene", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("engine.diagnostics.last_dropped_update_steps_scene", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("window.CodeRuntime", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("code-profile", StringComparison.Ordinal) &&
                !outputs.IndexHtml.Contains("\"callableNames\":", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("engine.audio.can_play_sound_scene", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("publishDiagnostics(", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("beginFixedUpdateStep()", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("\"typeName\": \"MainScene\"", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("\"virtualWidth\": 640", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("\"virtualHeight\": 360", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("\"drawHud\": {", StringComparison.Ordinal) &&
                !outputs.IndexHtml.Contains("fileInput", StringComparison.Ordinal) &&
                !outputs.IndexHtml.Contains("Load a compiled", StringComparison.Ordinal);

            if (!matched)
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-scene-runtime: generated web app did not match expected runtime contract");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-scene-runtime");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-scene-runtime: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"array<Boid> boids;
constant integer count = 2;
constant real twoPi = tau;

function start() {
  boids = new array<Boid>(count);
  foreach index in count then {
    boids[index] = new Boid(index * 10, random() * twoPi);
  }
}

function update() {
  foreach boid in boids then boid.update();
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
  foreach boid in boids then boid.draw();
}

object Boid {
  real x;
  real angle = twoPi;

  constructor(real startX, real startAngle) {
    x = startX;
    angle = startAngle;
  }

  function update() {
    angle += 0.01;
  }

  function draw() {
    Draw.circle(x, 20, 4, Colors.rgb(0, 128, 233));
  }
}"
                },
                "main.code");

            if (!outputs.IndexHtml.Contains("\"typeName\": \"MainScene\"", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-inferred-profile-global-state: generated metadata did not include MainScene");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-inferred-profile-global-state");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-inferred-profile-global-state: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["helper.code"] =
@"export function helper() {
  print(""helper"");
}

function start() {
}

function update() {
}

function draw() {
}",
                    ["main.code"] =
@"import { helper } from ""helper.code"";

export object MainScene {
  constructor() {
  }

  function start() {
    helper();
  }

  function update() {
  }

  function draw() {
  }
}"
                },
                "main.code");

            if (!outputs.IndexHtml.Contains("\"typeName\": \"MainScene\"", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-imported-lifecycle-names-not-special: generated metadata did not include MainScene");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-imported-lifecycle-names-not-special");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-imported-lifecycle-names-not-special: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"export object MainScene {
  constructor() {
  }

  function start() {
  }

  function update() {
  }

  function draw() {
    print(""hello web"");
  }
}"
                },
                "main.code",
                emitWebBytecode: true);

            bool matched =
                outputs.IndexHtmlExists &&
                outputs.BytecodeExists &&
                outputs.BytecodeLength > 0 &&
                outputs.OutputFiles.Any(path => string.Equals(path.Replace('\\', '/'), "code-runtime.wasm", StringComparison.OrdinalIgnoreCase)) &&
                outputs.IndexHtml.Contains("APP_BYTECODE_BASE64", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("new WorkerCodeRuntimeController", StringComparison.Ordinal);

            if (!matched)
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-emit-bytecode: generated web app did not emit expected app.bytecode artifact");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-emit-bytecode");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-emit-bytecode: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
}"
                },
                "main.code",
                directWasmBackend: true,
                disableDirectWasmGarbageCollection: true);

            bool matched =
                outputs.IndexHtmlExists &&
                outputs.OutputFiles.Any(path => string.Equals(path.Replace('\\', '/'), "code-app.wasm", StringComparison.OrdinalIgnoreCase)) &&
                outputs.IndexHtml.Contains("const CODE_WEB_BACKEND = \"direct-wasm\"", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("const CODE_DIRECT_WASM_OPTIONS = {\"garbageCollectionDisabled\":true", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("garbageCollectionMode\":\"disabled\"", StringComparison.Ordinal);

            if (!matched)
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-direct-wasm-disable-gc: generated web app did not include expected direct-Wasm GC metadata");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-direct-wasm-disable-gc");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-direct-wasm-disable-gc: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["helper.code"] =
@"export object HelperDrawable {
  Color color;

  constructor() {
    color = Colors.rgb(255, 255, 255);
  }

  implement WorldDrawable.draw() {
    Draw.rectangle(12, 12, 16, 16, color);
  }
}

export function<HelperDrawable> make_helper_drawable() {
  return new HelperDrawable();
}

export function<integer> play_helper_sound() {
  return Audio.playSound(""assets/click.wav"", 1);
}",
                    ["main.code"] =
@"import { HelperDrawable, make_helper_drawable, play_helper_sound } from ""helper.code"";

export object MainScene {
  Scene scene;
  SceneLoop loop;
  HelperDrawable helper_drawable;

  constructor() {
    scene = new Scene();
    loop = new SceneLoop(scene);
    helper_drawable = make_helper_drawable();
  }

  function start() {
    scene.addWorldDrawable(helper_drawable, 0);
    loop.start();
    play_helper_sound();
  }

  function update() {
    loop.update();
  }

  function draw() {
    loop.draw();
  }
}"
                },
                "main.code");

            if (!outputs.IndexHtml.Contains("\"typeName\": \"MainScene\"", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-implied-engine-imports-imported-module: generated metadata did not include MainScene");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-implied-engine-imports-imported-module");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-implied-engine-imports-imported-module: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "code-installed-lib-web-build-" + Guid.NewGuid().ToString("N"));
            string projectDir = Path.Combine(tempRoot, "project");
            string workDir = Path.Combine(tempRoot, "work");
            string oldCurrentDirectory = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(projectDir);
            Directory.CreateDirectory(workDir);
            try
            {
                string entryPath = Path.Combine(projectDir, "source.code");
                File.WriteAllText(
                    entryPath,
@"function start() {
}

function update() {
}

function draw() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
}");

                Directory.SetCurrentDirectory(workDir);
                var result = WebBuildPipeline.Build(entryPath, Path.Combine(workDir, "site"));

                if (!File.Exists(result.IndexHtmlPath))
                {
                    failures++;
                    Console.WriteLine("[FAIL] web-build-installed-compiler-bundled-lib-resolution: missing index.html");
                }
                else
                {
                    Console.WriteLine("[PASS] web-build-installed-compiler-bundled-lib-resolution");
                }
            }
            finally
            {
                Directory.SetCurrentDirectory(oldCurrentDirectory);
                try { Directory.Delete(tempRoot, recursive: true); }
                catch { }
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-installed-compiler-bundled-lib-resolution: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"import everything as Draw from ""engine/drawing.code"";
import everything as Colors from ""engine/colors.code"";

export object MainScene {
  constructor() {
  }

  function start() {
  }

  function update() {
  }

  function draw() {
    Draw.clearScreen(Colors.rgb(0, 0, 0));
  }
}"
                },
                "main.code");

            if (!outputs.IndexHtml.Contains("\"typeName\": \"MainScene\"", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine("[FAIL] web-build-explicit-canonical-engine-namespace-imports: generated metadata did not include MainScene");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-explicit-canonical-engine-namespace-imports");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-explicit-canonical-engine-namespace-imports: threw {ex.GetType().Name} - {ex.Message}");
        }

        ExpectWebBuildCompilerError(
            "web-build-missing-engine-library-diagnostic",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"import everything as Missing from ""engine/not_there.code"";

function start() {
}

function update() {
}

function draw() {
}"
            },
            "main.code",
            "bundled compiler library folder");

        ExpectWebBuildCompilerError(
            "web-build-requires-entry-shape",
            new Dictionary<string, string>
            {
                ["main.code"] = "print(1);"
            },
            "main.code",
            "Web build requires either an explicit object 'MainScene'");

        ExpectWebBuildCompilerError(
            "web-build-inferred-profile-missing-update",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function draw() {
}"
            },
            "main.code",
            "update()");

        ExpectWebBuildCompilerError(
            "web-build-inferred-profile-old-draw-hud-name",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
}

function draw_hud() {
}"
            },
            "main.code",
            "Use 'drawHud()'");

        ExpectWebBuildCompilerError(
            "web-build-inferred-profile-mixed-explicit-and-top-level",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"export object MainScene {
  constructor() {
  }

  function start() {
  }

  function update() {
  }

  function draw() {
  }
}

function start() {
}

function update() {
}

function draw() {
}"
            },
            "main.code",
            "cannot declare both an explicit 'MainScene' object and top-level lifecycle functions");

        ExpectWebBuildCompilerError(
            "web-build-explicit-main-scene-old-draw-hud-name",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"export object MainScene {
  constructor() {
  }

  function start() {
  }

  function update() {
  }

  function draw() {
  }

  function draw_hud() {
  }
}"
            },
            "main.code",
            "Use 'drawHud()'");

        ExpectWebBuildCompilerError(
            "web-build-inferred-profile-top-level-executable-statement",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"integer counter = 0;

function start() {
}

function update() {
}

function draw() {
}

print(counter);"
            },
            "main.code",
            "only allows state declarations");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-reserved-namespace-name",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"integer Draw = 0;

function start() {
}

function update() {
}

function draw() {
  print(Draw);
}"
            },
            "main.code",
            "reserve 'Draw' for implied engine imports");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-reserved-diagnostics-name",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"integer Diagnostics = 0;

function start() {
}

function update() {
}

function draw() {
  print(Diagnostics);
}"
            },
            "main.code",
            "reserve 'Diagnostics' for implied engine imports");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-reserved-audio-name",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"integer Audio = 0;

function start() {
}

function update() {
}

function draw() {
  print(Audio);
}"
            },
            "main.code",
            "reserve 'Audio' for implied engine imports");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-bare-engine-function",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
  rectangle(0, 0, 8, 8, Colors.rgb(255, 255, 255));
}"
            },
            "main.code",
            "Use 'Draw.rectangle(...)' or add an explicit import");

        ExpectWebBuildCompilerError(
            "web-build-old-input-key-name-rejected",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
  print(Input.key_is_down(37));
}

function draw() {
}"
            },
            "main.code",
            "Namespace 'Input' does not export function 'key_is_down'");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-bare-diagnostics-function",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
  print(lastFrameWorkMilliseconds());
}"
            },
            "main.code",
            "Use 'Diagnostics.lastFrameWorkMilliseconds(...)' or add an explicit import");

        ExpectWebBuildCompilerError(
            "web-build-implied-engine-imports-bare-audio-function",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
  print(playSound(""assets/click.wav"", 1));
}"
            },
            "main.code",
            "Use 'Audio.playSound(...)' or add an explicit import");

        ExpectWebBuildCompilerError(
            "web-build-engine-rgb-rejects-real-channels",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"function start() {
}

function update() {
}

function draw() {
  Draw.clearScreen(Colors.rgb(1. / 2, 0, 0));
}"
            },
            "main.code",
            "Argument 0 type mismatch");

        ExpectWebBuildCompilerError(
            "web-build-inferred-profile-constant-state-reassignment",
            new Dictionary<string, string>
            {
                ["main.code"] =
@"constant integer step = 1;

function start() {
}

function update() {
  step += 1;
}

function draw() {
}"
            },
            "main.code",
            "Cannot assign to constant 'step'");

        try
        {
            CompileModulesExpectError(
                new Dictionary<string, string>
                {
                    ["main.code"] =
@"function main() {
  Draw.clearScreen(Colors.rgb(0, 0, 0));
}"
                },
                "main.code",
                "Undefined variable 'Draw'",
                Compiler.CompileTarget.VmWeb);
            Console.WriteLine("[PASS] vm-web-without-build-does-not-imply-engine-imports");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] vm-web-without-build-does-not-imply-engine-imports: {ex.GetType().Name} - {ex.Message}");
        }

        return failures;
    }

    private static int RunExampleCatalogTests()
    {
        int failures = 0;

        var runnableCompileExamples = new List<(string Name, string RelativePath, Compiler.CompileTarget Target)>
        {
            ("example-arithmetic-runnable", @"ConsoleApp1/examples/arithmetic.code", Compiler.CompileTarget.VmNative),
            ("example-enum-runnable", @"ConsoleApp1/examples/enum.code", Compiler.CompileTarget.VmNative),
            ("example-record-runnable", @"ConsoleApp1/examples/record.code", Compiler.CompileTarget.VmNative),
            ("example-switch-runnable", @"ConsoleApp1/examples/switch.code", Compiler.CompileTarget.VmNative),
            ("example-fallible-runnable", @"ConsoleApp1/examples/fallible.code", Compiler.CompileTarget.VmNative),
            ("example-strings-runnable", @"ConsoleApp1/examples/strings.code", Compiler.CompileTarget.VmNative),
            ("example-forloop-runnable", @"ConsoleApp1/examples/forloop.code", Compiler.CompileTarget.VmNative),
            ("example-foreach-runnable", @"ConsoleApp1/examples/foreach.code", Compiler.CompileTarget.VmNative),
            ("example-arrayloop-runnable", @"ConsoleApp1/examples/arrayloop.code", Compiler.CompileTarget.VmNative),
            ("example-optional-runnable", @"ConsoleApp1/examples/optional.code", Compiler.CompileTarget.VmNative),
            ("example-time-runnable", @"ConsoleApp1/examples/time.code", Compiler.CompileTarget.VmNative),
            ("example-math-random-runnable", @"ConsoleApp1/examples/math_random.code", Compiler.CompileTarget.VmNative),
            ("example-sized-numerics-runnable", @"ConsoleApp1/examples/sized_numerics.code", Compiler.CompileTarget.VmNative),
            ("example-collections-runnable", @"ConsoleApp1/examples/collections.code", Compiler.CompileTarget.VmNative),
            ("example-object-runnable", @"ConsoleApp1/examples/object.code", Compiler.CompileTarget.VmNative),
            ("example-implicit-this-runnable", @"ConsoleApp1/examples/implicit_this.code", Compiler.CompileTarget.VmNative),
            ("example-interface-dispatch-runnable", @"ConsoleApp1/examples/interface_dispatch.code", Compiler.CompileTarget.VmNative),
            ("example-interface-array-dispatch-runnable", @"ConsoleApp1/examples/interface_array_dispatch.code", Compiler.CompileTarget.VmNative),
            ("example-modules-main-runnable", @"ConsoleApp1/examples/modules/main.code", Compiler.CompileTarget.VmNative),
            ("example-modules-grouped-imports-runnable", @"ConsoleApp1/examples/modules/grouped-imports.code", Compiler.CompileTarget.VmNative),
            ("example-modules-re-exports-runnable", @"ConsoleApp1/examples/modules/re_exports_main.code", Compiler.CompileTarget.VmNative),
            ("example-modules-visibility-runnable", @"ConsoleApp1/examples/modules/visibility_main.code", Compiler.CompileTarget.VmNative),
            ("example-modules-member-visibility-runnable", @"ConsoleApp1/examples/modules/member_visibility_main.code", Compiler.CompileTarget.VmNative),
        };

        foreach (var (name, relativePath, target) in runnableCompileExamples)
        {
            try
            {
                var bytes = CompileRepoExample(relativePath, target);
                if (bytes.Length <= 0)
                {
                    failures++;
                    Console.WriteLine($"[FAIL] {name}: compile produced no bytecode");
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

        try
        {
            string output = Normalize(CompileAndRunRepoPackageExample(
                @"ConsoleApp1/examples/package_manifest_host_requirements/ok/main.code",
                Compiler.CompileTarget.VmWeb,
                VmHostTarget.Web));
            if (!string.Equals(output, "ok package\n1\n1\n", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] example-package-host-requirements-ok: unexpected output '{Escape(output)}'");
            }
            else
            {
                Console.WriteLine("[PASS] example-package-host-requirements-ok");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-package-host-requirements-ok: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            VerifyRepoPackageArtifactExample(@"ConsoleApp1/examples/package_library_artifact/main.code");
            Console.WriteLine("[PASS] example-package-library-artifact");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-package-library-artifact: threw {ex.GetType().Name} - {ex.Message}");
        }

        var webBuildExamples = new List<(string Name, string RelativePath)>
        {
            ("example-audio-demo-web-build", @"ConsoleApp1/examples/audio_demo.code"),
            ("example-performance-dashboard-web-build", @"ConsoleApp1/examples/performance_dashboard.code"),
            ("example-shape-dodge-web-build", @"ConsoleApp1/examples/shape_dodge.code"),
            ("example-web-scene-web-build", @"ConsoleApp1/examples/web_scene.code"),
        };

        foreach (var (name, relativePath) in webBuildExamples)
        {
            try
            {
                VerifyRepoWebBuildExample(relativePath);
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[FAIL] {name}: threw {ex.GetType().Name} - {ex.Message}");
            }
        }

        try
        {
            CompileRepoExampleExpectCompileError(
                @"ConsoleApp1/examples/constants.code",
                "Cannot assign to constant");
            Console.WriteLine("[PASS] example-constants-negative");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-constants-negative: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            CompileRepoPackageExampleExpectCompileError(
                @"ConsoleApp1/examples/package_manifest_host_requirements/web_blocked/main.code",
                Compiler.CompileTarget.VmWeb,
                "hostAbi.requires");
            Console.WriteLine("[PASS] example-package-host-requirements-web-blocked");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-package-host-requirements-web-blocked: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            CompileRepoExampleExpectRuntimeError(
                @"ConsoleApp1/examples/panic.code",
                "UserError");
            Console.WriteLine("[PASS] example-panic-negative");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-panic-negative: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string catalogText = File.ReadAllText(GetRepoPath(@"docs/example-catalog.md"));
            bool matched =
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/enum.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/record.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/switch.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/fallible.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/strings.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/time.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/math_random.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/sized_numerics.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/collections.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/modules/visibility_main.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/modules/member_visibility_main.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `negative` | `ConsoleApp1/examples/constants.code` | `expected compile error` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/shape_dodge.code` | `build-web` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/web_scene.code` | `build-web` |", StringComparison.Ordinal);

            if (!matched)
            {
                failures++;
                Console.WriteLine("[FAIL] example-catalog-statuses: catalog rows did not match expected statuses");
            }
            else
            {
                Console.WriteLine("[PASS] example-catalog-statuses");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] example-catalog-statuses: threw {ex.GetType().Name} - {ex.Message}");
        }

        return failures;
    }

    private static int RunTargetParityTests()
    {
        int failures = 0;
        string source =
@"print(unixMilliseconds() > 0);
print(unixMicroseconds() > 0);
print(monotonicNanoseconds() >= 0);
print(monotonicTicks() > 0);
print(monotonicTicksPerSecond() > 0);";

        try
        {
            string nativeOutput = Normalize(CompileAndRun(source, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(source, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            if (!string.Equals(nativeOutput, webOutput, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-time-print: native '{Escape(nativeOutput)}' web '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-time-print");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-time-print: threw {ex.GetType().Name} - {ex.Message}");
        }

        string mathSource =
@"print(minimum(4, 9));
print(maximum(4, 9));
print(absolute(-3));
print(sign(-3));
print(sign(0));
print(sign(3));
print(lerp(10, 20, 1. / 4));
print(sine(0));
print(cosine(0));
print(squareRoot(81));
real value = random();
print(value >= 0 and value < 1);";

        try
        {
            string nativeOutput = Normalize(CompileAndRun(mathSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(mathSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            const string expected = "4\n9\n3\n-1\n0\n1\n12.5\n0\n1\n9\n1\n";
            if (!string.Equals(nativeOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-math-print: native expected '{Escape(expected)}' got '{Escape(nativeOutput)}'");
            }
            else if (!string.Equals(webOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-math-print: web expected '{Escape(expected)}' got '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-math-print");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-math-print: threw {ex.GetType().Name} - {ex.Message}");
        }

        string numericPolishSource =
@"enum Direction {
  Left = 1;
  Right = 2;
}
print(1.5 + .5);
print(3.8 as integer);
print(-3.8 as integer);
print(Direction.Right as integer);
Direction direction = (1 + 1) as Direction;
print(direction == Direction.Right);
whole32 whole_max = 4294967295;
byte channel = 250;
channel += 5;
print(whole_max);
print(channel);
print(3.8 as integer8);
real32 rounded = 1.25 as real32;
print(rounded);";

        try
        {
            string nativeOutput = Normalize(CompileAndRun(numericPolishSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(numericPolishSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            const string expected = "2\n3\n-3\n2\n1\n4294967295\n255\n3\n1.25\n";
            if (!string.Equals(nativeOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-numeric-polish: native expected '{Escape(expected)}' got '{Escape(nativeOutput)}'");
            }
            else if (!string.Equals(webOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-numeric-polish: web expected '{Escape(expected)}' got '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-numeric-polish");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-numeric-polish: threw {ex.GetType().Name} - {ex.Message}");
        }

        string engineSource =
@"whole window = windowCreate(""demo"", 320, 200);
print(window > 0);
print(windowShouldClose(window));
print(windowInputKeyDown(window, 13));
gfxClear(window, 0, 0, 0, 1);
gfxDrawRectangle(window, 1, 2, 3, 4, 1, 0, 0, 1);
windowPresent(window);
print(diagnosticsLastFrameIntervalMilliseconds());
print(diagnosticsLastUpdateSteps());
print(diagnosticsLastDroppedUpdateSteps());
print(audioCanPlaySound());
print(audioPlaySound(""assets/click.wav"", 1));
print(audioPlayLoopingSound(""assets/loop.wav"", 1));
print(audioSoundIsPlaying(1));
audioSetSoundVolume(1, 1);
audioStopSound(1);
audioStopAllSounds();
print(1);";

        try
        {
            string nativeOutput = Normalize(CompileAndRun(engineSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(engineSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            if (!string.Equals(nativeOutput, webOutput, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-engine-stubs: native '{Escape(nativeOutput)}' web '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-engine-stubs");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-engine-stubs: threw {ex.GetType().Name} - {ex.Message}");
        }

        string recordSource =
@"interface Reader {
  function<integer> read();
}
record Point {
  integer x;
  integer y;
  constructor(integer x, integer y) {
    this.x = x;
    this.y = y;
  }
  function<Point> moved(integer amount) {
    x += amount;
    return this;
  }
  implement Reader.read() {
    return x;
  }
}
Point left = new Point(1, 2);
Point right = new Point(1, 2);
set<Point> points = new set<Point>();
points.add(left);
map<Point, integer> scores = new map<Point, integer>();
scores[left] = 9;
Reader reader = left.moved(3);
print(left == right);
print(points.contains(right));
print(scores[right]);
print(reader.read());";

        try
        {
            const string expected = "1\n1\n9\n4\n";
            string nativeOutput = Normalize(CompileAndRun(recordSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(recordSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            if (!string.Equals(nativeOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-record-values: native expected '{Escape(expected)}' got '{Escape(nativeOutput)}'");
            }
            else if (!string.Equals(webOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-record-values: web expected '{Escape(expected)}' got '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-record-values");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-record-values: threw {ex.GetType().Name} - {ex.Message}");
        }

        string fallibleSource =
@"enum LoadError {
  Missing;
  Invalid;
}
function<fallible<integer, LoadError>> read(boolean ok) {
  if ok then return 4;
  return error(LoadError.Invalid, ""bad data"");
}
function<fallible<integer>> read_quick(boolean ok) {
  if ok then return 2;
  return error(""quick miss"");
}
print(read(true) on error {
  yield 0;
});
integer value = read(false) on error {
  print(error.message);
  switch error.code then {
    case LoadError.Missing then yield 1;
    case LoadError.Invalid then yield 9;
    default then yield 0;
  }
};
print(value);
print(read_quick(true) on error {
  yield 0;
});
integer quick = read_quick(false) on error {
  print(error.code);
  print(error.message);
  yield 7;
};
print(quick);";

        try
        {
            const string expected = "4\nbad data\n9\n2\n0\nquick miss\n7\n";
            string nativeOutput = Normalize(CompileAndRun(fallibleSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(fallibleSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            if (!string.Equals(nativeOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-fallible-values: native expected '{Escape(expected)}' got '{Escape(nativeOutput)}'");
            }
            else if (!string.Equals(webOutput, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] target-parity-fallible-values: web expected '{Escape(expected)}' got '{Escape(webOutput)}'");
            }
            else
            {
                Console.WriteLine("[PASS] target-parity-fallible-values");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] target-parity-fallible-values: threw {ex.GetType().Name} - {ex.Message}");
        }

        return failures;
    }

    private static int RunWebRuntimeParityTests()
    {
        int failures = 0;

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);

            var versionMatch = Regex.Match(
                runtimeText,
                @"const\s+BYTECODE_VERSION\s*=\s*(?<version>\d+)\s*;",
                RegexOptions.CultureInvariant);
            if (!versionMatch.Success)
                throw new Exception("Could not find BYTECODE_VERSION in web runtime.");

            byte jsBytecodeVersion = Convert.ToByte(versionMatch.Groups["version"].Value, CultureInfo.InvariantCulture);
            if (jsBytecodeVersion != BytecodeFormat.Version)
                throw new Exception($"Web runtime bytecode version {jsBytecodeVersion} does not match native version {BytecodeFormat.Version}.");

            var opcodeBlockMatch = Regex.Match(
                runtimeText,
                @"const\s+OpCode\s*=\s*\{(?<body>.*?)\n\};",
                RegexOptions.Singleline | RegexOptions.CultureInvariant);

            if (!opcodeBlockMatch.Success)
                throw new Exception("Could not find OpCode table in web runtime.");

            var jsOpcodeMap = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                opcodeBlockMatch.Groups["body"].Value,
                @"(?m)^\s*([A-Za-z][A-Za-z0-9_]*)\s*:\s*0x([0-9a-fA-F]+)\s*,?\s*$",
                RegexOptions.CultureInvariant))
            {
                jsOpcodeMap[match.Groups[1].Value] = Convert.ToByte(match.Groups[2].Value, 16);
            }

            var jsSwitchCases = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(
                runtimeText,
                @"case\s+OpCode\.([A-Za-z][A-Za-z0-9_]*)\s*:",
                RegexOptions.CultureInvariant))
            {
                jsSwitchCases.Add(match.Groups[1].Value);
            }

            foreach (OpCode opcode in Enum.GetValues<OpCode>())
            {
                string name = opcode.ToString();
                byte expectedValue = (byte)opcode;

                if (!jsOpcodeMap.TryGetValue(name, out var actualValue))
                    throw new Exception($"Web runtime is missing opcode table entry '{name}'.");

                if (actualValue != expectedValue)
                    throw new Exception($"Web runtime opcode '{name}' has value 0x{actualValue:X2}, expected 0x{expectedValue:X2}.");

                if (!jsSwitchCases.Contains(name))
                    throw new Exception($"Web runtime is missing switch handler for opcode '{name}'.");
            }

            Console.WriteLine("[PASS] web-runtime-opcode-parity");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-opcode-parity: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);
            string[] requiredSymbols =
            {
                "std.math.minimum",
                "std.math.maximum",
                "std.math.absolute",
                "std.math.sign",
                "std.math.lerp",
                "std.math.sine",
                "std.math.cosine",
                "std.math.square_root",
                "std.math.random"
            };

            foreach (string symbol in requiredSymbols)
            {
                string marker = $"this.hostBindings.set(\"{symbol}\"";
                if (!runtimeText.Contains(marker, StringComparison.Ordinal))
                    throw new Exception($"Web runtime is missing host binding '{symbol}'.");
            }

            Console.WriteLine("[PASS] web-runtime-math-host-bindings");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-math-host-bindings: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);
            string[] requiredSymbols =
            {
                "engine.input.pointer_world_x_scene",
                "engine.input.pointer_world_y_scene",
                "engine.input.pointer_screen_x_scene",
                "engine.input.pointer_screen_y_scene",
                "engine.input.pointer_is_down_scene",
                "engine.input.pointer_was_pressed_scene",
                "engine.input.pointer_was_released_scene"
            };

            foreach (string symbol in requiredSymbols)
            {
                string marker = $"this.hostBindings.set(\"{symbol}\"";
                if (!runtimeText.Contains(marker, StringComparison.Ordinal))
                    throw new Exception($"Web runtime is missing host binding '{symbol}'.");
            }

            bool hasPointerRuntime =
                runtimeText.Contains("onPointerDown", StringComparison.Ordinal) &&
                runtimeText.Contains("pointerWorldX()", StringComparison.Ordinal) &&
                runtimeText.Contains("pointerScreenX / this.worldScale + this.viewLeft", StringComparison.Ordinal) &&
                runtimeText.Contains("touchAction = \"none\"", StringComparison.Ordinal);
            if (!hasPointerRuntime)
                throw new Exception("Web runtime is missing primary pointer tracking or coordinate conversion support.");

            Console.WriteLine("[PASS] web-runtime-pointer-host-bindings");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-pointer-host-bindings: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);
            string[] requiredSymbols =
            {
                "engine.diagnostics.last_frame_interval_milliseconds_scene",
                "engine.diagnostics.estimated_frames_per_second_scene",
                "engine.diagnostics.last_frame_work_milliseconds_scene",
                "engine.diagnostics.last_update_work_milliseconds_scene",
                "engine.diagnostics.last_draw_work_milliseconds_scene",
                "engine.diagnostics.last_draw_hud_work_milliseconds_scene",
                "engine.diagnostics.last_update_steps_scene",
                "engine.diagnostics.last_dropped_update_steps_scene",
                "engine.diagnostics.last_update_interval_milliseconds_scene",
                "engine.diagnostics.update_delta_milliseconds_scene"
            };

            foreach (string symbol in requiredSymbols)
            {
                string marker = $"this.hostBindings.set(\"{symbol}\"";
                if (!runtimeText.Contains(marker, StringComparison.Ordinal))
                    throw new Exception($"Web runtime is missing host binding '{symbol}'.");
            }

            bool hasDiagnosticsRuntime =
                runtimeText.Contains("publishDiagnostics(", StringComparison.Ordinal) &&
                runtimeText.Contains("lastFrameWorkMilliseconds()", StringComparison.Ordinal) &&
                runtimeText.Contains("lastUpdateSteps()", StringComparison.Ordinal) &&
                runtimeText.Contains("lastDroppedUpdateSteps()", StringComparison.Ordinal) &&
                runtimeText.Contains("lastUpdateIntervalMilliseconds()", StringComparison.Ordinal) &&
                runtimeText.Contains("updateDeltaMilliseconds()", StringComparison.Ordinal) &&
                runtimeText.Contains("maxUpdateStepsPerFrame = 5", StringComparison.Ordinal) &&
                runtimeText.Contains("performance.now() - frameWorkStartMs", StringComparison.Ordinal);
            if (!hasDiagnosticsRuntime)
                throw new Exception("Web runtime is missing frame diagnostics measurement support.");

            Console.WriteLine("[PASS] web-runtime-diagnostics-host-bindings");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-diagnostics-host-bindings: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);
            string[] requiredSymbols =
            {
                "engine.audio.can_play_sound_scene",
                "engine.audio.play_sound_scene",
                "engine.audio.play_looping_sound_scene",
                "engine.audio.stop_sound_scene",
                "engine.audio.set_sound_volume_scene",
                "engine.audio.sound_is_playing_scene",
                "engine.audio.stop_all_sounds_scene"
            };

            foreach (string symbol in requiredSymbols)
            {
                string marker = $"this.hostBindings.set(\"{symbol}\"";
                if (!runtimeText.Contains(marker, StringComparison.Ordinal))
                    throw new Exception($"Web runtime is missing host binding '{symbol}'.");
            }

            bool hasAudioRuntime =
                runtimeText.Contains("this.audioHandles = new Map()", StringComparison.Ordinal) &&
                runtimeText.Contains("this.pendingAudioHandles = new Set()", StringComparison.Ordinal) &&
                runtimeText.Contains("unlockAudio()", StringComparison.Ordinal) &&
                runtimeText.Contains("flushPendingAudio()", StringComparison.Ordinal) &&
                runtimeText.Contains("clampUnit(volume)", StringComparison.Ordinal) &&
                runtimeText.Contains("stopAllSounds()", StringComparison.Ordinal) &&
                runtimeText.Contains("soundIsPlaying(handle)", StringComparison.Ordinal);
            if (!hasAudioRuntime)
                throw new Exception("Web runtime is missing handle-based audio playback support.");

            Console.WriteLine("[PASS] web-runtime-audio-host-bindings");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-audio-host-bindings: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);
            bool hasDirectWasmGcMetadata =
                runtimeText.Contains("garbageCollectionDisabled", StringComparison.Ordinal) &&
                runtimeText.Contains("garbageCollectionEnabled", StringComparison.Ordinal) &&
                runtimeText.Contains("garbageCollectionMode", StringComparison.Ordinal) &&
                runtimeText.Contains("directWasmOptions", StringComparison.Ordinal);
            if (!hasDirectWasmGcMetadata)
                throw new Exception("Web runtime is missing direct-Wasm GC mode profiler metadata.");

            Console.WriteLine("[PASS] web-runtime-direct-wasm-gc-metadata");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-runtime-direct-wasm-gc-metadata: {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "test-browser-compat.mjs");
            string scriptText = File.ReadAllText(scriptPath);
            bool hasCompatibilitySuite =
                scriptText.Contains("CODE_BROWSER_COMPAT_BACKEND", StringComparison.Ordinal) &&
                scriptText.Contains("CODE_DIRECT_WASM_DISABLE_GC", StringComparison.Ordinal) &&
                scriptText.Contains("mobile-report.html", StringComparison.Ordinal) &&
                scriptText.Contains("browser-compat-report.json", StringComparison.Ordinal) &&
                scriptText.Contains("--web-backend", StringComparison.Ordinal) &&
                scriptText.Contains("direct-wasm", StringComparison.Ordinal);
            if (!hasCompatibilitySuite)
                throw new Exception("Browser compatibility suite is missing expected direct-Wasm report hooks.");

            Console.WriteLine("[PASS] browser-compat-suite-static-contract");
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] browser-compat-suite-static-contract: {ex.GetType().Name} - {ex.Message}");
        }

        return failures;
    }

    private static int RunHostAbiSurfaceTests()
    {
        int failures = 0;

        try
        {
            string output = Normalize(CompileAndRun("print(readLine());", input: "hello\n"));
            if (!string.Equals(output, "hello\n", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] read-line-native: expected 'hello\\n' got '{Escape(output)}'");
            }
            else
            {
                Console.WriteLine("[PASS] read-line-native");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] read-line-native: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            string output = Normalize(CompileAndRun("sleepMilliseconds(0); print(1);"));
            if (!string.Equals(output, "1\n", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] sleep-ms-native: expected '1\\n' got '{Escape(output)}'");
            }
            else
            {
                Console.WriteLine("[PASS] sleep-ms-native");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] sleep-ms-native: threw {ex.GetType().Name} - {ex.Message}");
        }

        string engineSource =
@"whole window = windowCreate(""demo"", 640, 480);
print(window > 0);
print(windowShouldClose(window));
print(windowInputKeyDown(window, 32));
print(inputPointerWorldX());
print(inputPointerWorldY());
print(inputPointerScreenX());
print(inputPointerScreenY());
print(inputPointerIsDown());
print(inputPointerWasPressed());
print(inputPointerWasReleased());
print(audioCanPlaySound());
print(audioPlaySound(""assets/click.wav"", 1));
print(audioPlayLoopingSound(""assets/loop.wav"", 1));
print(audioSoundIsPlaying(1));
audioSetSoundVolume(1, 1);
audioStopSound(1);
audioStopAllSounds();
gfxClear(window, 0, 0, 0, 1);
gfxDrawRectangle(window, 0, 0, 10, 10, 1, 0, 0, 1);
windowPresent(window);
print(1);";

        try
        {
            string output = Normalize(CompileAndRun(engineSource));
            const string expected = "1\n1\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n0\n1\n";
            if (!string.Equals(output, expected, StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] engine-host-stubs-native: expected '{Escape(expected)}' got '{Escape(output)}'");
            }
            else
            {
                Console.WriteLine("[PASS] engine-host-stubs-native");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] engine-host-stubs-native: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            _ = CompileAndRun(
                "print(readLine());",
                Compiler.CompileTarget.VmNative,
                VmHostTarget.Web);
            failures++;
            Console.WriteLine("[FAIL] read-line-web-runtime-diagnostic: expected runtime error");
        }
        catch (VmRuntimeException vex)
        {
            bool okType = string.Equals(vex.Error.Type, "HostBindingError", StringComparison.Ordinal);
            bool okMessage =
                vex.Message.Contains("vm-web", StringComparison.Ordinal) &&
                vex.Message.Contains("native-only", StringComparison.Ordinal);
            if (!okType || !okMessage)
            {
                failures++;
                Console.WriteLine($"[FAIL] read-line-web-runtime-diagnostic: unexpected error '{vex.Error.Type}' '{vex.Message}'");
            }
            else
            {
                Console.WriteLine("[PASS] read-line-web-runtime-diagnostic");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] read-line-web-runtime-diagnostic: threw {ex.GetType().Name} - {ex.Message}");
        }

        try
        {
            _ = CompileAndRun(
                "sleepMilliseconds(1); print(1);",
                Compiler.CompileTarget.VmNative,
                VmHostTarget.Web);
            failures++;
            Console.WriteLine("[FAIL] sleep-ms-web-runtime-diagnostic: expected runtime error");
        }
        catch (VmRuntimeException vex)
        {
            bool okType = string.Equals(vex.Error.Type, "HostBindingError", StringComparison.Ordinal);
            bool okMessage =
                vex.Message.Contains("vm-web", StringComparison.Ordinal) &&
                vex.Message.Contains("native-only", StringComparison.Ordinal);
            if (!okType || !okMessage)
            {
                failures++;
                Console.WriteLine($"[FAIL] sleep-ms-web-runtime-diagnostic: unexpected error '{vex.Error.Type}' '{vex.Message}'");
            }
            else
            {
                Console.WriteLine("[PASS] sleep-ms-web-runtime-diagnostic");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] sleep-ms-web-runtime-diagnostic: threw {ex.GetType().Name} - {ex.Message}");
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

    private static string CompileAndRun(
        string source,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative,
        VmHostTarget hostTarget = VmHostTarget.Native,
        string? input = null)
    {
        var bytes = Compiler.ModuleCompiler.CompileFromSource(source, target);
        using var sw = new StringWriter();
        using var sr = input is null ? null : new StringReader(input);
        var vm = new Vm(bytes, sw, hostTarget: hostTarget, input: sr);
        vm.Run();
        return sw.ToString();
    }

    private static string CompileAndRunModules(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-mod-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            var options = new Compiler.ModuleCompileOptions { Target = target };
            var bytes = Compiler.ModuleCompiler.CompileFromFile(entryPath, options);
            using var sw = new StringWriter();
            var vm = new Vm(bytes, sw);
            vm.Run();
            return sw.ToString();
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static ModuleToolingOutputs CompileModulesWithMetadata(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-mod-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            var traceLines = new List<string>();
            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            var options = new Compiler.ModuleCompileOptions
            {
                Target = target,
                TraceLinker = true,
                TraceWriter = message => traceLines.Add(message)
            };
            var result = Compiler.ModuleCompiler.CompileFromFileWithMetadata(entryPath, options);
            return new ModuleToolingOutputs(
                result.Graph.ToDisplayString(tempRoot),
                result.Graph.ToJsonString(tempRoot),
                result.Graph.ToDotString(tempRoot),
                string.Join("\n", traceLines));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static string CompileModulesAndReadLockfile(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-mod-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            var options = new Compiler.ModuleCompileOptions { Target = target };
            _ = Compiler.ModuleCompiler.CompileFromFile(entryPath, options);

            string lockPath = Path.Combine(tempRoot, "code.lock.json");
            if (!File.Exists(lockPath))
                throw new Exception("Expected code.lock.json to be generated");

            return File.ReadAllText(lockPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private sealed record ArtifactOutputs(string ArtifactJson, string LockfileJson, string RunOutput);

    private sealed record WebBuildOutputs(
        string OutputDirectory,
        string IndexHtmlPath,
        string BytecodePath,
        bool IndexHtmlExists,
        bool BytecodeExists,
        int BytecodeLength,
        string IndexHtml,
        IReadOnlyList<string> OutputFiles);

    private static ArtifactOutputs CompileModulesAndReadArtifact(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-mod-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            var options = new Compiler.ModuleCompileOptions { Target = target };
            _ = Compiler.ModuleCompiler.CompileFromFile(entryPath, options);

            var artifactFiles = Directory.GetFiles(tempRoot, "*.codelib", SearchOption.TopDirectoryOnly);
            if (artifactFiles.Length == 0)
                throw new Exception("Expected library artifact file was not generated");
            string artifactPath = artifactFiles[0];

            string lockPath = Path.Combine(tempRoot, "code.lock.json");
            if (!File.Exists(lockPath))
                throw new Exception("Expected code.lock.json to be generated");

            var artifact = Compiler.CodeLibraryArtifactFormat.Read(artifactPath);
            using var sw = new StringWriter();
            var vm = new Vm(artifact.Bytecode, sw);
            vm.Run();

            return new ArtifactOutputs(File.ReadAllText(artifactPath), File.ReadAllText(lockPath), sw.ToString());
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static WebBuildOutputs BuildWebApp(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        string? outputDirectory = null,
        bool emitWebBytecode = false,
        bool directWasmBackend = false,
        bool disableDirectWasmGarbageCollection = false)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-web-build-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            string? resolvedOutputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
                ? null
                : Path.GetFullPath(Path.Combine(tempRoot, outputDirectory));

            var result = WebBuildPipeline.Build(
                entryPath,
                resolvedOutputDirectory,
                emitWebBytecode: emitWebBytecode,
                directWasmBackend: directWasmBackend,
                disableDirectWasmGarbageCollection: disableDirectWasmGarbageCollection);
            bool indexHtmlExists = File.Exists(result.IndexHtmlPath);
            string bytecodePath = result.BytecodePath ?? Path.Combine(result.OutputDirectory, "app.bytecode");
            bool bytecodeExists = File.Exists(bytecodePath);
            string indexHtml = indexHtmlExists ? File.ReadAllText(result.IndexHtmlPath) : string.Empty;
            int bytecodeLength = bytecodeExists ? File.ReadAllBytes(bytecodePath).Length : 0;
            var outputFiles = Directory.GetFiles(result.OutputDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(result.OutputDirectory, path))
                .ToList();

            return new WebBuildOutputs(
                result.OutputDirectory,
                result.IndexHtmlPath,
                bytecodePath,
                indexHtmlExists,
                bytecodeExists,
                bytecodeLength,
                indexHtml,
                outputFiles);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static string GetRepoPath(string relativePath)
    {
        string normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        string path = Path.Combine(Directory.GetCurrentDirectory(), normalizedRelativePath);
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"Expected repo path was not found: {path}", path);
        return path;
    }

    private static byte[] CompileRepoExample(string relativePath, Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string path = GetRepoPath(relativePath);
        var options = new Compiler.ModuleCompileOptions { Target = target };
        return Compiler.ModuleCompiler.CompileFromFile(path, options);
    }

    private static void CompileRepoExampleExpectCompileError(string relativePath, string expectedContains, Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        try
        {
            _ = CompileRepoExample(relativePath, target);
            throw new Exception("Expected compile error was not thrown");
        }
        catch (Compiler.CompilerException ex)
        {
            if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                throw new Exception($"Expected compile error containing '{expectedContains}', got '{ex.Message}'");
        }
    }

    private static void CompileRepoExampleExpectRuntimeError(
        string relativePath,
        string expectedType,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative,
        VmHostTarget hostTarget = VmHostTarget.Native)
    {
        var bytes = CompileRepoExample(relativePath, target);
        using var sw = new StringWriter();
        var vm = new Vm(bytes, sw, hostTarget: hostTarget);
        try
        {
            vm.Run();
            throw new Exception("Expected runtime error was not thrown");
        }
        catch (VmRuntimeException vex)
        {
            if (!string.Equals(vex.Error.Type, expectedType, StringComparison.Ordinal))
                throw new Exception($"Expected runtime error '{expectedType}', got '{vex.Error.Type}'");
        }
    }

    private static void VerifyRepoWebBuildExample(string relativePath)
    {
        string outputDirectory = Path.Combine(Path.GetTempPath(), "code-example-web-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string examplePath = GetRepoPath(relativePath);
            var result = WebBuildPipeline.Build(examplePath, outputDirectory);
            string bytecodePath = result.BytecodePath ?? Path.Combine(result.OutputDirectory, "app.bytecode");

            bool matched =
                File.Exists(result.IndexHtmlPath) &&
                result.BytecodePath is null &&
                !File.Exists(bytecodePath) &&
                File.ReadAllText(result.IndexHtmlPath).Contains("MainScene", StringComparison.Ordinal);

            if (!matched)
                throw new Exception("Build output missing expected files");
        }
        finally
        {
            try { Directory.Delete(outputDirectory, recursive: true); }
            catch { }
        }
    }

    private static string CompileAndRunRepoPackageExample(
        string relativeEntryPath,
        Compiler.CompileTarget target,
        VmHostTarget hostTarget)
    {
        var workspace = CopyRepoPackageExampleToTemp(relativeEntryPath);
        try
        {
            var options = new Compiler.ModuleCompileOptions { Target = target };
            var bytes = Compiler.ModuleCompiler.CompileFromFile(workspace.EntryPath, options);
            using var sw = new StringWriter();
            var vm = new Vm(bytes, sw, hostTarget: hostTarget);
            vm.Run();
            return sw.ToString();
        }
        finally
        {
            try { Directory.Delete(workspace.TempRoot, recursive: true); }
            catch { }
        }
    }

    private static void CompileRepoPackageExampleExpectCompileError(
        string relativeEntryPath,
        Compiler.CompileTarget target,
        string expectedContains)
    {
        var workspace = CopyRepoPackageExampleToTemp(relativeEntryPath);
        try
        {
            try
            {
                var options = new Compiler.ModuleCompileOptions { Target = target };
                _ = Compiler.ModuleCompiler.CompileFromFile(workspace.EntryPath, options);
                throw new Exception("Expected compile error was not thrown");
            }
            catch (Compiler.CompilerException ex)
            {
                if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                    throw new Exception($"Expected compile error containing '{expectedContains}', got '{ex.Message}'");
            }
        }
        finally
        {
            try { Directory.Delete(workspace.TempRoot, recursive: true); }
            catch { }
        }
    }

    private static void VerifyRepoPackageArtifactExample(string relativeEntryPath)
    {
        var workspace = CopyRepoPackageExampleToTemp(relativeEntryPath);
        try
        {
            var options = new Compiler.ModuleCompileOptions { Target = Compiler.CompileTarget.VmNative };
            _ = Compiler.ModuleCompiler.CompileFromFile(workspace.EntryPath, options);

            var artifactFiles = Directory.GetFiles(workspace.TempRoot, "*.codelib", SearchOption.TopDirectoryOnly);
            if (artifactFiles.Length == 0)
                throw new Exception("Expected library artifact file was not generated");

            string lockPath = Path.Combine(workspace.TempRoot, "code.lock.json");
            if (!File.Exists(lockPath))
                throw new Exception("Expected code.lock.json to be generated");
        }
        finally
        {
            try { Directory.Delete(workspace.TempRoot, recursive: true); }
            catch { }
        }
    }

    private static TempPackageWorkspace CopyRepoPackageExampleToTemp(string relativeEntryPath)
    {
        string repoEntryPath = GetRepoPath(relativeEntryPath);
        string entryDirectory = Path.GetDirectoryName(repoEntryPath)
            ?? throw new InvalidOperationException($"Could not determine directory for '{repoEntryPath}'.");
        string packageRoot = FindNearestManifestDirectory(entryDirectory)
            ?? throw new DirectoryNotFoundException($"Could not find code.package.json for '{repoEntryPath}'.");

        string tempRoot = Path.Combine(Path.GetTempPath(), "code-package-example-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(packageRoot, tempRoot);

        string relativeEntry = Path.GetRelativePath(packageRoot, repoEntryPath);
        string tempEntryPath = Path.Combine(tempRoot, relativeEntry);
        return new TempPackageWorkspace(tempRoot, tempEntryPath);
    }

    private static string? FindNearestManifestDirectory(string startDirectory)
    {
        string repoRoot = Directory.GetCurrentDirectory();
        string? current = startDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "code.package.json")))
                return current;

            if (string.Equals(current, repoRoot, StringComparison.OrdinalIgnoreCase))
                break;

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var sourcePath in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            string destinationPath = Path.Combine(destinationRoot, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }
    }

    private static bool ContainsAll(string testName, string sectionName, string text, IReadOnlyList<string> expectedPieces)
    {
        for (int i = 0; i < expectedPieces.Count; i++)
        {
            if (!text.Contains(expectedPieces[i], StringComparison.Ordinal))
            {
                Console.WriteLine($"[FAIL] {testName}: {sectionName} missing '{expectedPieces[i]}'");
                return false;
            }
        }

        return true;
    }

    private sealed record ModuleToolingOutputs(
        string GraphText,
        string GraphJson,
        string GraphDot,
        string TraceOutput);

    private sealed record TempPackageWorkspace(string TempRoot, string EntryPath);

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
            _ = Compiler.ModuleCompiler.CompileFromSource(source);
            throw new Exception("Expected compile error was not thrown");
        }
        catch (Compiler.CompilerException ex)
        {
            if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                throw new Exception($"Expected compile error containing '{expectedContains}', got '{ex.Message}'");
        }
    }

    private static void CompileModulesExpectError(
        IReadOnlyDictionary<string, string> files,
        string entryRelativePath,
        string expectedContains,
        Compiler.CompileTarget target = Compiler.CompileTarget.VmNative)
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "code-mod-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            PopulateWorkspace(tempRoot, files);

            string entryPath = Path.Combine(tempRoot, entryRelativePath);
            try
            {
                var options = new Compiler.ModuleCompileOptions { Target = target };
                _ = Compiler.ModuleCompiler.CompileFromFile(entryPath, options);
                throw new Exception("Expected module compile error was not thrown");
            }
            catch (Compiler.CompilerException ex)
            {
                if (!ex.Message.Contains(expectedContains, StringComparison.Ordinal))
                    throw new Exception($"Expected compile error containing '{expectedContains}', got '{ex.Message}'");
            }
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static void PopulateWorkspace(string tempRoot, IReadOnlyDictionary<string, string> files)
    {
        CopyBundledLib(tempRoot);

        foreach (var pair in files)
        {
            string fullPath = Path.Combine(tempRoot, pair.Key);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, pair.Value);
        }
    }

    private static void CopyBundledLib(string tempRoot)
    {
        string bundledLibRoot = Path.Combine(Directory.GetCurrentDirectory(), "lib");
        if (!Directory.Exists(bundledLibRoot))
            return;

        string destinationRoot = Path.Combine(tempRoot, "lib");
        foreach (var sourceFile in Directory.GetFiles(bundledLibRoot, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(bundledLibRoot, sourceFile);
            string destinationPath = Path.Combine(destinationRoot, relativePath);
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir))
                Directory.CreateDirectory(destinationDir);
            File.Copy(sourceFile, destinationPath, overwrite: true);
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n");
}
