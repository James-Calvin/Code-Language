using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ConsoleApp1.Compiler;

[Flags]
enum HostAbiTargets
{
    None = 0,
    Native = 1,
    Web = 2,
    All = Native | Web
}

sealed record HostAbiSymbol(
    string Symbol,
    int Arity,
    string Capability,
    HostAbiTargets Targets);

sealed record HostAbiIntrinsic(
    string Name,
    HostAbiSymbol Symbol,
    TypeSymbol ReturnType,
    string ReturnTypeName,
    IReadOnlyList<TypeSymbol> ParameterTypes,
    IReadOnlyList<string> ParameterTypeNames)
{
    public int Arity => Symbol.Arity;
}

static class HostAbiCatalog
{
    private static readonly IReadOnlyDictionary<string, HostAbiSymbol> Symbols = BuildSymbols();

    private static readonly IReadOnlyDictionary<string, HostAbiIntrinsic> Intrinsics = BuildIntrinsics();

    public static HostAbiSymbol StandardInputOutputPrint => Symbols["standard.input_output.print"];

    public static IEnumerable<HostAbiIntrinsic> IntrinsicSignatures => Intrinsics.Values;

    public static bool TryGetSymbol(string symbol, out HostAbiSymbol hostSymbol)
        => Symbols.TryGetValue(symbol, out hostSymbol!);

    public static bool TryGetIntrinsic(string intrinsicName, out HostAbiIntrinsic intrinsic)
        => Intrinsics.TryGetValue(intrinsicName, out intrinsic!);

    public static IEnumerable<string> IntrinsicNames => Intrinsics.Keys;

    public static bool IsSupported(HostAbiSymbol symbol, CompileTarget target)
    {
        HostAbiTargets needed = target switch
        {
            CompileTarget.VmNative => HostAbiTargets.Native,
            CompileTarget.VmWeb => HostAbiTargets.Web,
            _ => HostAbiTargets.None
        };

        return (symbol.Targets & needed) != 0;
    }

    private static IReadOnlyDictionary<string, HostAbiSymbol> BuildSymbols()
    {
        var map = new Dictionary<string, HostAbiSymbol>(StringComparer.Ordinal)
        {
            ["standard.input_output.print"] = new HostAbiSymbol("standard.input_output.print", 1, "standard.input_output", HostAbiTargets.All),
            ["standard.input_output.read_line"] = new HostAbiSymbol("standard.input_output.read_line", 0, "standard.input_output.read_line", HostAbiTargets.Native),
            ["std.io.print"] = new HostAbiSymbol("std.io.print", 1, "standard.input_output", HostAbiTargets.All),
            ["std.io.read_line"] = new HostAbiSymbol("std.io.read_line", 0, "standard.input_output.read_line", HostAbiTargets.Native),
            ["std.time.unix_ms"] = new HostAbiSymbol("std.time.unix_ms", 0, "std.time", HostAbiTargets.All),
            ["std.time.unix_us"] = new HostAbiSymbol("std.time.unix_us", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ns"] = new HostAbiSymbol("std.time.mono_ns", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks"] = new HostAbiSymbol("std.time.mono_ticks", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks_per_second"] = new HostAbiSymbol("std.time.mono_ticks_per_second", 0, "std.time", HostAbiTargets.All),
            ["std.time.sleep_ms"] = new HostAbiSymbol("std.time.sleep_ms", 1, "std.time.sleep_ms", HostAbiTargets.Native),
            ["std.math.minimum"] = new HostAbiSymbol("std.math.minimum", 2, "std.math", HostAbiTargets.All),
            ["std.math.maximum"] = new HostAbiSymbol("std.math.maximum", 2, "std.math", HostAbiTargets.All),
            ["std.math.absolute"] = new HostAbiSymbol("std.math.absolute", 1, "std.math", HostAbiTargets.All),
            ["std.math.sign"] = new HostAbiSymbol("std.math.sign", 1, "std.math", HostAbiTargets.All),
            ["std.math.lerp"] = new HostAbiSymbol("std.math.lerp", 3, "std.math", HostAbiTargets.All),
            ["std.math.sine"] = new HostAbiSymbol("std.math.sine", 1, "std.math", HostAbiTargets.All),
            ["std.math.cosine"] = new HostAbiSymbol("std.math.cosine", 1, "std.math", HostAbiTargets.All),
            ["std.math.random"] = new HostAbiSymbol("std.math.random", 0, "std.math", HostAbiTargets.All),

            ["engine.window.create"] = new HostAbiSymbol("engine.window.create", 3, "engine.window", HostAbiTargets.All),
            ["engine.window.should_close"] = new HostAbiSymbol("engine.window.should_close", 1, "engine.window", HostAbiTargets.All),
            ["engine.window.present"] = new HostAbiSymbol("engine.window.present", 1, "engine.window", HostAbiTargets.All),

            ["engine.input.key_down"] = new HostAbiSymbol("engine.input.key_down", 2, "engine.input", HostAbiTargets.All),
            ["engine.input.key_down_scene"] = new HostAbiSymbol("engine.input.key_down_scene", 1, "engine.input", HostAbiTargets.All),

            ["engine.window.camera_view_left_scene"] = new HostAbiSymbol("engine.window.camera_view_left_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_top_scene"] = new HostAbiSymbol("engine.window.camera_view_top_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_width_scene"] = new HostAbiSymbol("engine.window.camera_view_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_height_scene"] = new HostAbiSymbol("engine.window.camera_view_height_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_right_scene"] = new HostAbiSymbol("engine.window.camera_view_right_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_view_bottom_scene"] = new HostAbiSymbol("engine.window.camera_view_bottom_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_left_scene"] = new HostAbiSymbol("engine.window.camera_safe_left_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_top_scene"] = new HostAbiSymbol("engine.window.camera_safe_top_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_width_scene"] = new HostAbiSymbol("engine.window.camera_safe_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_height_scene"] = new HostAbiSymbol("engine.window.camera_safe_height_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_right_scene"] = new HostAbiSymbol("engine.window.camera_safe_right_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.camera_safe_bottom_scene"] = new HostAbiSymbol("engine.window.camera_safe_bottom_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.screen_width_scene"] = new HostAbiSymbol("engine.window.screen_width_scene", 0, "engine.window", HostAbiTargets.All),
            ["engine.window.screen_height_scene"] = new HostAbiSymbol("engine.window.screen_height_scene", 0, "engine.window", HostAbiTargets.All),

            ["engine.gfx.clear"] = new HostAbiSymbol("engine.gfx.clear", 5, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.clear_scene"] = new HostAbiSymbol("engine.gfx.clear_scene", 4, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rect"] = new HostAbiSymbol("engine.gfx.draw_rect", 9, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rect_scene"] = new HostAbiSymbol("engine.gfx.draw_rect_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rectangle_scene"] = new HostAbiSymbol("engine.gfx.draw_rectangle_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_rectangle_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_rectangle_outline_scene", 9, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_circle_scene"] = new HostAbiSymbol("engine.gfx.draw_circle_scene", 7, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_circle_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_circle_outline_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_polygon_scene"] = new HostAbiSymbol("engine.gfx.draw_polygon_scene", 5, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_polygon_outline_scene"] = new HostAbiSymbol("engine.gfx.draw_polygon_outline_scene", 6, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_line_scene"] = new HostAbiSymbol("engine.gfx.draw_line_scene", 8, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_text_scene"] = new HostAbiSymbol("engine.gfx.draw_text_scene", 10, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_image_scene"] = new HostAbiSymbol("engine.gfx.draw_image_scene", 6, "engine.gfx", HostAbiTargets.All),
            ["engine.gfx.draw_sprite_scene"] = new HostAbiSymbol("engine.gfx.draw_sprite_scene", 10, "engine.gfx", HostAbiTargets.All)
        };

        return new ReadOnlyDictionary<string, HostAbiSymbol>(map);
    }

    private static IReadOnlyDictionary<string, HostAbiIntrinsic> BuildIntrinsics()
    {
        static HostAbiIntrinsic Sig(
            string name,
            string symbol,
            TypeSymbol returnType,
            string returnTypeName,
            params (TypeSymbol Symbol, string Name)[] parameters)
        {
            var paramTypes = new List<TypeSymbol>(parameters.Length);
            var paramTypeNames = new List<string>(parameters.Length);
            for (int i = 0; i < parameters.Length; i++)
            {
                paramTypes.Add(parameters[i].Symbol);
                paramTypeNames.Add(parameters[i].Name);
            }

            return new HostAbiIntrinsic(
                name,
                Symbols[symbol],
                returnType,
                returnTypeName,
                paramTypes,
                paramTypeNames);
        }

        var map = new Dictionary<string, HostAbiIntrinsic>(StringComparer.Ordinal)
        {
            ["unix_ms"] = Sig("unix_ms", "std.time.unix_ms", TypeSymbol.Integer, "integer"),
            ["unix_us"] = Sig("unix_us", "std.time.unix_us", TypeSymbol.Integer, "integer"),
            ["mono_ns"] = Sig("mono_ns", "std.time.mono_ns", TypeSymbol.Integer, "integer"),
            ["mono_ticks"] = Sig("mono_ticks", "std.time.mono_ticks", TypeSymbol.Integer, "integer"),
            ["mono_ticks_per_second"] = Sig("mono_ticks_per_second", "std.time.mono_ticks_per_second", TypeSymbol.Integer, "integer"),
            ["sleep_ms"] = Sig("sleep_ms", "std.time.sleep_ms", TypeSymbol.Void, "void", (TypeSymbol.Integer, "integer")),
            ["read_line"] = Sig("read_line", "standard.input_output.read_line", TypeSymbol.String, "string"),
            ["minimum"] = Sig("minimum", "std.math.minimum", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["maximum"] = Sig("maximum", "std.math.maximum", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["absolute"] = Sig("absolute", "std.math.absolute", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["sign"] = Sig("sign", "std.math.sign", TypeSymbol.Integer, "integer", (TypeSymbol.Real, "real")),
            ["lerp"] = Sig("lerp", "std.math.lerp", TypeSymbol.Real, "real", (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real"), (TypeSymbol.Real, "real")),
            ["sine"] = Sig("sine", "std.math.sine", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["cosine"] = Sig("cosine", "std.math.cosine", TypeSymbol.Real, "real", (TypeSymbol.Real, "real")),
            ["random"] = Sig("random", "std.math.random", TypeSymbol.Real, "real"),

            ["window_create"] = Sig(
                "window_create",
                "engine.window.create",
                TypeSymbol.Whole,
                "whole",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Integer, "integer"),
                (TypeSymbol.Integer, "integer")),
            ["window_should_close"] = Sig(
                "window_should_close",
                "engine.window.should_close",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Whole, "whole")),
            ["window_present"] = Sig(
                "window_present",
                "engine.window.present",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole")),
            ["input_key_down"] = Sig(
                "input_key_down",
                "engine.input.key_down",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Integer, "integer")),
            ["key_down"] = Sig(
                "key_down",
                "engine.input.key_down_scene",
                TypeSymbol.Boolean,
                "boolean",
                (TypeSymbol.Integer, "integer")),
            ["camera_view_left"] = Sig("camera_view_left", "engine.window.camera_view_left_scene", TypeSymbol.Real, "real"),
            ["camera_view_top"] = Sig("camera_view_top", "engine.window.camera_view_top_scene", TypeSymbol.Real, "real"),
            ["camera_view_width"] = Sig("camera_view_width", "engine.window.camera_view_width_scene", TypeSymbol.Real, "real"),
            ["camera_view_height"] = Sig("camera_view_height", "engine.window.camera_view_height_scene", TypeSymbol.Real, "real"),
            ["camera_view_right"] = Sig("camera_view_right", "engine.window.camera_view_right_scene", TypeSymbol.Real, "real"),
            ["camera_view_bottom"] = Sig("camera_view_bottom", "engine.window.camera_view_bottom_scene", TypeSymbol.Real, "real"),
            ["camera_safe_left"] = Sig("camera_safe_left", "engine.window.camera_safe_left_scene", TypeSymbol.Real, "real"),
            ["camera_safe_top"] = Sig("camera_safe_top", "engine.window.camera_safe_top_scene", TypeSymbol.Real, "real"),
            ["camera_safe_width"] = Sig("camera_safe_width", "engine.window.camera_safe_width_scene", TypeSymbol.Real, "real"),
            ["camera_safe_height"] = Sig("camera_safe_height", "engine.window.camera_safe_height_scene", TypeSymbol.Real, "real"),
            ["camera_safe_right"] = Sig("camera_safe_right", "engine.window.camera_safe_right_scene", TypeSymbol.Real, "real"),
            ["camera_safe_bottom"] = Sig("camera_safe_bottom", "engine.window.camera_safe_bottom_scene", TypeSymbol.Real, "real"),
            ["screen_width"] = Sig("screen_width", "engine.window.screen_width_scene", TypeSymbol.Real, "real"),
            ["screen_height"] = Sig("screen_height", "engine.window.screen_height_scene", TypeSymbol.Real, "real"),
            ["gfx_clear"] = Sig(
                "gfx_clear",
                "engine.gfx.clear",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["clear"] = Sig(
                "clear",
                "engine.gfx.clear_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["gfx_draw_rect"] = Sig(
                "gfx_draw_rect",
                "engine.gfx.draw_rect",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Whole, "whole"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_rectangle"] = Sig(
                "draw_rectangle",
                "engine.gfx.draw_rectangle_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_rect"] = Sig(
                "draw_rect",
                "engine.gfx.draw_rectangle_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_rectangle_outline"] = Sig(
                "draw_rectangle_outline",
                "engine.gfx.draw_rectangle_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_circle"] = Sig(
                "draw_circle",
                "engine.gfx.draw_circle_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_circle_outline"] = Sig(
                "draw_circle_outline",
                "engine.gfx.draw_circle_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_polygon"] = Sig(
                "draw_polygon",
                "engine.gfx.draw_polygon_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Array, "array<real>"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_polygon_outline"] = Sig(
                "draw_polygon_outline",
                "engine.gfx.draw_polygon_outline_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Array, "array<real>"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_line"] = Sig(
                "draw_line",
                "engine.gfx.draw_line_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_text"] = Sig(
                "draw_text",
                "engine.gfx.draw_text_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.String, "string"),
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_image"] = Sig(
                "draw_image",
                "engine.gfx.draw_image_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real")),
            ["draw_sprite"] = Sig(
                "draw_sprite",
                "engine.gfx.draw_sprite_scene",
                TypeSymbol.Void,
                "void",
                (TypeSymbol.String, "string"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"),
                (TypeSymbol.Real, "real"))
        };

        return new ReadOnlyDictionary<string, HostAbiIntrinsic>(map);
    }
}
