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

    public static HostAbiSymbol StdIoPrint => Symbols["std.io.print"];

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
            ["std.io.print"] = new HostAbiSymbol("std.io.print", 1, "std.io", HostAbiTargets.All),
            ["std.io.read_line"] = new HostAbiSymbol("std.io.read_line", 0, "std.io.read_line", HostAbiTargets.Native),
            ["std.time.unix_ms"] = new HostAbiSymbol("std.time.unix_ms", 0, "std.time", HostAbiTargets.All),
            ["std.time.unix_us"] = new HostAbiSymbol("std.time.unix_us", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ns"] = new HostAbiSymbol("std.time.mono_ns", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks"] = new HostAbiSymbol("std.time.mono_ticks", 0, "std.time", HostAbiTargets.All),
            ["std.time.mono_ticks_per_second"] = new HostAbiSymbol("std.time.mono_ticks_per_second", 0, "std.time", HostAbiTargets.All),
            ["std.time.sleep_ms"] = new HostAbiSymbol("std.time.sleep_ms", 1, "std.time.sleep_ms", HostAbiTargets.Native),

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
            ["engine.gfx.draw_rect_scene"] = new HostAbiSymbol("engine.gfx.draw_rect_scene", 8, "engine.gfx", HostAbiTargets.All)
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
            ["read_line"] = Sig("read_line", "std.io.read_line", TypeSymbol.String, "string"),

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
            ["draw_rect"] = Sig(
                "draw_rect",
                "engine.gfx.draw_rect_scene",
                TypeSymbol.Void,
                "void",
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
