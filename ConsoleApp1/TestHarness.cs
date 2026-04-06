using System;
using System.Collections.Generic;
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
                "1" + nl)
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
print(value);", "5\n15\n7.5\n6.5\n2.5\n"),
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
            ("constant-ok", @"constant real PI = 3; print(PI);", "3\n")
            ,
            ("time-intrinsics",
@"print(unix_ms() > 0);
print(unix_us() > 0);
print(mono_ns() >= 0);
print(mono_ticks() > 0);
print(mono_ticks_per_second() > 0);",
             "1\n1\n1\n1\n1\n")
            ,
            ("math-random-intrinsics",
@"print(minimum(4, 9));
print(maximum(4, 9));
print(absolute(-3));
print(sign(-3));
print(sign(0));
print(sign(3));
print(lerp(10, 20, 1 / 4));
print(sine(0));
print(cosine(0));
real value = random();
print(value >= 0 and value < 1);",
             "4\n9\n3\n-1\n0\n1\n12.5\n0\n1\n1\n")
            ,
            ("legacy-draw-rectangle-alias",
@"draw_rect(0, 0, 8, 8, 1, 1, 1, 1);
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
            ("array-append-remove", @"array<integer> items = new array<integer>(0); items.append(10); items.append(20); items.remove_at(0); print(items.length); print(items[0]);", "1\n20\n"),
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
            ("scene-intrinsics-native-stubs",
@"object MainScene {
  constructor() { }
  function start() {
    print(""start"");
  }
  function update() {
    print(key_down(37));
  }
  function draw() {
    clear(0, 0, 0, 1);
    draw_rectangle(camera_view_left(), camera_view_top(), 30, 40, 1, 1, 1, 1);
    draw_rectangle_outline(camera_view_left() + 4, camera_view_top() + 4, 20, 30, 2, 1, 1, 1, 1);
    draw_circle(camera_safe_left() + 20, camera_safe_top() + 20, 10, 1, 1, 1, 1);
    draw_circle_outline(camera_safe_left() + 20, camera_safe_top() + 20, 16, 2, 1, 1, 1, 1);
    draw_polygon({0, 0, 10, 0, 5, 10}, 1, 1, 1, 1);
    draw_polygon_outline({0, 0, 10, 0, 5, 10}, 2, 1, 1, 1, 1);
    draw_line(camera_safe_left(), camera_safe_top(), camera_safe_right(), camera_safe_bottom(), 1, 1, 1, 1);
    draw_image(""assets/example.svg"", 0, 0, 16, 16, 1);
    draw_sprite(""assets/example.svg"", 0, 0, 8, 8, 16, 16, 8, 8, 1);
    print(camera_view_left());
    print(camera_view_top());
    print(camera_view_width());
    print(camera_view_height());
    print(camera_view_right());
    print(camera_view_bottom());
    print(camera_safe_width());
    print(camera_safe_height());
  }
  function draw_hud() {
    draw_rectangle(screen_width() - 10, screen_height() - 10, 8, 8, 1, 1, 1, 1);
    draw_text(""hud"", screen_width() - 12, 12, 12, ""right"", ""top"", 1, 1, 1, 1);
    print(screen_width());
    print(screen_height());
  }
}
MainScene scene = new MainScene();
scene.start();
scene.update();
scene.draw();
scene.draw_hud();", "start\n0\n0\n0\n640\n360\n640\n360\n640\n360\n640\n360\n"),
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
import { rgb } from ""engine/colors.code"";
import { key_is_down } from ""engine/input.code"";
Draw.clear_screen(rgb(0, 0, 0));
Draw.rectangle(10, 10, 12, 14, rgb(1, 1, 1));
Draw.line(0, 0, 10, 10, rgb(1, 1, 1));
Draw.circle(20, 20, 8, rgb(1, 1, 1));
Draw.polygon({0, 0, 12, 0, 6, 12}, rgb(1, 1, 1));
Draw.image(""assets/test.svg"", 0, 0, 16, 16, 1);
Draw.sprite(""assets/test.svg"", 0, 0, 8, 8, 20, 20, 8, 8, 1);
Draw.text(""ok"", Viewport.hud_width() - 10, 10, 12, ""right"", ""top"", rgb(1, 1, 1));
print(Viewport.hud_width());
print(key_is_down(37));",
                    ["assets/test.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"></svg>",
                },
                "main.code",
                "640\n0\n"
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
scene.add_world_drawable(new Layer(), 0);
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
scene.add_world_drawable(new Upper(), 10);
scene.add_world_drawable(new Lower(), 0);
scene.add_updatable(new Counter());
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
      scene.add_startable(child);
      scene.add_updatable(child);
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
scene.add_updatable(spawner);
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
      scene.remove_updatable(child);
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
scene.add_updatable(child);
scene.add_updatable(remover);
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
                    ["main.code"] = "print(unix_ms());",
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
                    ["main.code"] = "sleep_ms(0);\nprint(read_line());",
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
@"constant integer PI = 3;
PI = 4;", "Cannot assign to constant 'PI'"),
            ("constant-missing-init", @"constant integer value;", "must be initialized"),
            ("time-intrinsic-arity", @"print(unix_ms(1));", "expects 0 args"),
            ("math-intrinsic-arity", @"print(minimum(1));", "expects 2 args"),
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
                    ["main.code"] = "print(read_line());",
                },
                "main.code",
                "Capability 'standard.input_output.read_line' is not available for target 'vm-web'"
            ),
            (
                "module-target-web-rejects-sleep-ms-intrinsic",
                Compiler.CompileTarget.VmWeb,
                new Dictionary<string, string>
                {
                    ["main.code"] = "sleep_ms(1); print(1);",
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

        try
        {
            var outputs = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["assets/code-sheet.svg"] =
"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"64\" height=\"32\"><rect width=\"32\" height=\"32\" fill=\"#0b1020\"/><rect x=\"32\" width=\"32\" height=\"32\" fill=\"#1d4ed8\"/></svg>",
                    ["main.code"] =
@"import everything as Draw from ""engine/drawing.code"";
import everything as Viewport from ""engine/viewport.code"";
import { rgb } from ""engine/colors.code"";
import { key_is_down } from ""engine/input.code"";

export object MainScene {
  integer x;
  integer y;
  integer speed;

  constructor() {
    this.x = 100;
    this.y = 120;
    this.speed = 2;
  }

  function start() {
  }

  function update() {
    if key_is_down(37) then this.x -= this.speed;
    if key_is_down(39) then this.x += this.speed;
    if key_is_down(38) then this.y -= this.speed;
    if key_is_down(40) then this.y += this.speed;
  }

  function draw() {
    Draw.clear_screen(rgb(0, 0, 0));
    Draw.line(Viewport.safe_left(), Viewport.safe_top(), Viewport.safe_right(), Viewport.safe_bottom(), rgb(1, 1, 1));
    Draw.polygon({300, 80, 340, 92, 352, 120, 304, 124, 284, 100}, rgb(1, 1, 1));
    Draw.circle(124, 84, 16, rgb(1, 1, 1));
    Draw.image(""assets/code-sheet.svg"", 24, 220, 64, 32, 1);
    Draw.sprite(""assets/code-sheet.svg"", 32, 0, 32, 32, 104, 210, 64, 64, 1);
    if this.x > Viewport.view_left() - 24 and this.x < Viewport.view_right() then {
      Draw.rectangle(this.x, this.y, 24, 24, rgb(1, 1, 1));
    }
  }

  function draw_hud() {
    Draw.text(""Code"", 16, 16, 18, ""left"", ""top"", rgb(1, 1, 1));
    Draw.text(""Arrow keys move"", Viewport.hud_width() - 16, 16, 16, ""right"", ""top"", rgb(1, 1, 1));
  }
}"
                },
                "main.code");

            bool matched =
                string.Equals(Path.GetFileName(outputs.OutputDirectory), "dist", StringComparison.OrdinalIgnoreCase) &&
                outputs.IndexHtmlExists &&
                outputs.BytecodeExists &&
                outputs.BytecodeLength > 0 &&
                outputs.OutputFiles.Any(path => string.Equals(
                    path.Replace('\\', '/'),
                    "assets/code-sheet.svg",
                    StringComparison.OrdinalIgnoreCase)) &&
                outputs.IndexHtml.Contains("CanvasSceneRuntime", StringComparison.Ordinal) &&
                outputs.IndexHtml.Contains("APP_METADATA", StringComparison.Ordinal) &&
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
            _ = BuildWebApp(
                new Dictionary<string, string>
                {
                    ["main.code"] = "print(1);"
                },
                "main.code");
            failures++;
            Console.WriteLine("[FAIL] web-build-requires-main-scene: expected compile error");
        }
        catch (Compiler.CompilerException ex)
        {
            if (!ex.Message.Contains("Web build requires object 'MainScene'", StringComparison.Ordinal))
            {
                failures++;
                Console.WriteLine($"[FAIL] web-build-requires-main-scene: unexpected error '{ex.Message}'");
            }
            else
            {
                Console.WriteLine("[PASS] web-build-requires-main-scene");
            }
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine($"[FAIL] web-build-requires-main-scene: threw {ex.GetType().Name} - {ex.Message}");
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
            ("example-time-runnable", @"ConsoleApp1/examples/time.code", Compiler.CompileTarget.VmNative),
            ("example-math-random-runnable", @"ConsoleApp1/examples/math_random.code", Compiler.CompileTarget.VmNative),
            ("example-object-runnable", @"ConsoleApp1/examples/object.code", Compiler.CompileTarget.VmNative),
            ("example-implicit-this-runnable", @"ConsoleApp1/examples/implicit_this.code", Compiler.CompileTarget.VmNative),
            ("example-interface-dispatch-runnable", @"ConsoleApp1/examples/interface_dispatch.code", Compiler.CompileTarget.VmNative),
            ("example-interface-array-dispatch-runnable", @"ConsoleApp1/examples/interface_array_dispatch.code", Compiler.CompileTarget.VmNative),
            ("example-modules-main-runnable", @"ConsoleApp1/examples/modules/main.code", Compiler.CompileTarget.VmNative),
            ("example-modules-grouped-imports-runnable", @"ConsoleApp1/examples/modules/grouped-imports.code", Compiler.CompileTarget.VmNative),
            ("example-modules-re-exports-runnable", @"ConsoleApp1/examples/modules/re_exports_main.code", Compiler.CompileTarget.VmNative),
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
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/time.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `runnable` | `ConsoleApp1/examples/math_random.code` | `run` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `negative` | `ConsoleApp1/examples/constants.code` | `expected compile error` |", StringComparison.Ordinal) &&
                catalogText.Contains("| `planned` | `ConsoleApp1/examples/record.code` | `planned only` |", StringComparison.Ordinal) &&
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
@"print(unix_ms() > 0);
print(unix_us() > 0);
print(mono_ns() >= 0);
print(mono_ticks() > 0);
print(mono_ticks_per_second() > 0);";

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
print(lerp(10, 20, 1 / 4));
print(sine(0));
print(cosine(0));
real value = random();
print(value >= 0 and value < 1);";

        try
        {
            string nativeOutput = Normalize(CompileAndRun(mathSource, Compiler.CompileTarget.VmNative, VmHostTarget.Native));
            string webOutput = Normalize(CompileAndRun(mathSource, Compiler.CompileTarget.VmWeb, VmHostTarget.Web));
            const string expected = "4\n9\n3\n-1\n0\n1\n12.5\n0\n1\n1\n";
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

        string engineSource =
@"whole window = window_create(""demo"", 320, 200);
print(window > 0);
print(window_should_close(window));
print(input_key_down(window, 13));
gfx_clear(window, 0, 0, 0, 1);
gfx_draw_rect(window, 1, 2, 3, 4, 1, 0, 0, 1);
window_present(window);
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

        return failures;
    }

    private static int RunWebRuntimeParityTests()
    {
        int failures = 0;

        try
        {
            string runtimePath = Path.Combine(Directory.GetCurrentDirectory(), "web-runtime", "code-vm-web.js");
            string runtimeText = File.ReadAllText(runtimePath);

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

        return failures;
    }

    private static int RunHostAbiSurfaceTests()
    {
        int failures = 0;

        try
        {
            string output = Normalize(CompileAndRun("print(read_line());", input: "hello\n"));
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
            string output = Normalize(CompileAndRun("sleep_ms(0); print(1);"));
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
@"whole window = window_create(""demo"", 640, 480);
print(window > 0);
print(window_should_close(window));
print(input_key_down(window, 32));
gfx_clear(window, 0, 0, 0, 1);
gfx_draw_rect(window, 0, 0, 10, 10, 1, 0, 0, 1);
window_present(window);
print(1);";

        try
        {
            string output = Normalize(CompileAndRun(engineSource));
            const string expected = "1\n1\n0\n1\n";
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
                "print(read_line());",
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
                "sleep_ms(1); print(1);",
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
        string? outputDirectory = null)
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

            var result = WebBuildPipeline.Build(entryPath, resolvedOutputDirectory);
            bool indexHtmlExists = File.Exists(result.IndexHtmlPath);
            bool bytecodeExists = File.Exists(result.BytecodePath);
            string indexHtml = indexHtmlExists ? File.ReadAllText(result.IndexHtmlPath) : string.Empty;
            int bytecodeLength = bytecodeExists ? File.ReadAllBytes(result.BytecodePath).Length : 0;
            var outputFiles = Directory.GetFiles(result.OutputDirectory, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(result.OutputDirectory, path))
                .ToList();

            return new WebBuildOutputs(
                result.OutputDirectory,
                result.IndexHtmlPath,
                result.BytecodePath,
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

            bool matched =
                File.Exists(result.IndexHtmlPath) &&
                File.Exists(result.BytecodePath) &&
                File.ReadAllBytes(result.BytecodePath).Length > 0 &&
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

